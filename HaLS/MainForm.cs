/* Copyright (C) 2015 haha01haha01

* This Source Code Form is subject to the terms of the Mozilla Public
* License, v. 2.0. If a copy of the MPL was not distributed with this
* file, You can obtain one at http://mozilla.org/MPL/2.0/. */

using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace HaLS
{
    public partial class MainForm : Form
    {
        public static bool refresh = false;

        public MainForm()
        {
            InitializeComponent();
            sleepBox.Text = sleep.ToString();
        }

        void Log(string text)
        {
            Invoke((Action)delegate
            {
                logList.Items.Add(text);
                logList.SelectedIndex = logList.Items.Count - 1;
            });
        }

        void SetSpeed(double speed)
        {
            Invoke((Action)delegate
            {
                speedLabel.Text = speed.ToString();
            });
        }

        byte[] DownloadDataSafe(string url)
        {
            Log("Downloading " + url);
            while (true)
            {
                try
                {
                    HttpWebRequest req = (HttpWebRequest)HttpWebRequest.Create(url);
                    req.UserAgent = "Mozilla/5.0 (Windows NT 6.1; WOW64; rv:38.0) Gecko/20100101 Firefox/38.0";
                    req.Accept = "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8";
                    req.Headers["Accept-Language"] = "en-US,en;q=0.5";
                    req.Headers["Accept-Encoding"] = "gzip, deflate";

                    // Keepalive hack
                    req.KeepAlive = true;
                    var sp = req.ServicePoint;
                    var prop = sp.GetType().GetProperty("HttpBehaviour", BindingFlags.Instance | BindingFlags.NonPublic);
                    prop.SetValue(sp, (byte)0, null);
                    // End hack

                    byte[] buf;
                    string encoding;
                    using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                    {
                        long len = resp.ContentLength;
                        buf = new byte[len];
                        encoding = resp.ContentEncoding;
                        Stream s = resp.GetResponseStream();
                        long tot_read = 0;
                        while (tot_read < len)
                        {
                            int cur_read = s.Read(buf, (int)tot_read, (int)(Math.Min(len - tot_read, 1048576)));
                            if (cur_read <= 0)
                            {
                                throw new Exception();
                            }
                            tot_read += cur_read;
                            if (refresh)
                            {
                                refresh = false;
                                throw new RefreshException();
                            }
                        }
                    }
                    Stream decomp;
                    switch (encoding)
                    {
                        case "":
                            return buf;
                        case "gzip":
                            decomp = new GZipStream(new MemoryStream(buf), CompressionMode.Decompress);
                            break;
                        case "deflate":
                            decomp = new DeflateStream(new MemoryStream(buf), CompressionMode.Decompress);
                            break;
                        default:
                            MessageBox.Show(encoding);
                            throw new NotSupportedException();
                    }
                    MemoryStream outStream = new MemoryStream();
                    int count = 0;
                    byte[] compBuf = new byte[1048576];
                    do
                    {
                        count = decomp.Read(compBuf, 0, compBuf.Length);
                        outStream.Write(compBuf, 0, count);
                    }
                    while (count > 0);
                    if (count != 0)
                    {
                        throw new Exception();
                    }
                    byte[] outBuf = outStream.ToArray();
                    return outBuf;
                }
                catch (RefreshException)
                {
                    throw;
                }
                catch (Exception)
                {
                    Log("Retrying " + url);
                }
            }
        }

        private bool IsValidLine(string line)
        {
            return line.Length > 0 && line[0] != '#';
        }

        public const string IndexSig = "index-";
        public const string StartTsSig = "start_offset=";
        public const string EndTsSig = "end_offset=";
        public const bool debug = false;

        private TsFileInfo GetInfoFromLine(string line)
        {
            string[] args = line.Split("?&".ToCharArray());
            if (args.Length != 3 || !args[0].StartsWith(IndexSig) || !args[1].StartsWith(StartTsSig) || !args[2].StartsWith(EndTsSig))
            {
                throw new ArgumentException();
            }
            TsFileInfo result;
            result.name = args[0];
            result.start_offset = args[1];
            result.end_offset = args[2];
            result.size = 1 + long.Parse(result.end_offset.Substring(EndTsSig.Length)) - long.Parse(result.start_offset.Substring(StartTsSig.Length));
            return result;
        }

        private void InitProgress(int len)
        {
            Invoke((Action)delegate
            {
                progressBar.Maximum = len;
                progressBar.Value = 0;
            });
        }

        private void AdvanceProgress()
        {
            Invoke((Action)delegate
            {
                progressBar.Value++;
            });
        }

        private async Task WriteAllBytesAsync(string filename, byte[] data)
        {
            using (FileStream fs = File.Create(filename))
            {
                await fs.WriteAsync(data, 0, data.Length);
            }
        }

        private string ParseID(string id)
        {
            if (id.Contains("/"))
                id = id.Substring(id.LastIndexOf("/") + 1);
            return id;
        }

        private string GetVodPlaylist(string id)
        {
            string authdata = Encoding.UTF8.GetString(DownloadDataSafe("https://api.twitch.tv/api/vods/" + id + "/access_token"));
            AuthData authdata_obj = JsonConvert.DeserializeObject<AuthData>(authdata);
            string pl_opts = Encoding.UTF8.GetString(DownloadDataSafe("http://usher.justin.tv/vod/" + id + "?allow_source=true&player=twitchweb&nauth=" + authdata_obj.token + "&nauthsig=" + authdata_obj.sig));
            List<string> opts = pl_opts.Split("\r\n".ToCharArray(), StringSplitOptions.RemoveEmptyEntries).Where(x => IsValidLine(x)).ToList();
            QualitySelect qs = new QualitySelect(opts);
            qs.ShowDialog();
            return qs.selection;
        }

        private byte[] DownloadDataUberSafe(string url)
        {
            while (true)
            {
                try
                {
                    return DownloadDataSafe(url);
                }
                catch (RefreshException)
                {
                    Log("Refreshing!");
                    Thread.Sleep(30000);
                }
            }
        }

        private const string StateFileName = "state.txt";

        private void SaveState(int i, long pos)
        {
            File.WriteAllLines(StateFileName, new string[] { i.ToString(), pos.ToString() });
        }

        private int GetBytePieceIndex(List<TsFileInfo> pieces, double ratio)
        {
            int i = 0;
            List<long> piece_sizes = pieces.Select(x => x.size).ToList();
            long our_size = (long)(piece_sizes.Sum() * ratio);
            long summed_size = 0;
            while (our_size > summed_size + piece_sizes[i])
            {
                summed_size += piece_sizes[i++];
            }
            return i;
        }

        private void DownloadPlaylist(string url, string outdir, string id, double ratio, double eratio)
        {
            // Get general info about the playlist
            string hls_pl = Encoding.UTF8.GetString(DownloadDataSafe(url));
            string hls_base = url.Remove(url.LastIndexOf('/') + 1);
            List<TsFileInfo> pieces = GetPieces(hls_pl);

            // Start GUI features (progressbar and download speed)
            InitProgress(pieces.Count);
            Stopwatch sw = new Stopwatch();
            sw.Start();
            long totBytes = 0;

            // We now have all pieces and need to just download and concat them
            string tspath = Path.Combine(outdir, id + ".ts");
            int i_start = 0;
            long pos = 0;

            // If there is a backup, try to restore
            if (File.Exists(StateFileName) && MessageBox.Show("Restore backup?", "Backup", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                int[] data = File.ReadAllLines(StateFileName).Select(x => int.Parse(x)).ToArray();
                i_start = data[0];
                pos = data[1];
            }
            else
            {
                i_start = GetBytePieceIndex(pieces, ratio);
            }
            int i_end = GetBytePieceIndex(pieces, eratio) + 1;
            Log(string.Format("Fast tracked to piece {0}, ending at {1}/{2}", i_start, i_end, pieces.Count));

            // Actual DL
            using (FileStream fs = new FileStream(tspath, pos == 0 ? FileMode.Create : FileMode.Open))
            {
                if (pos != 0)
                    fs.Seek(pos, SeekOrigin.Begin);

                for (int i = 0; i < i_start; i++)
                {
                    AdvanceProgress();
                }

                for (int i = i_start; i < i_end; i++)
                {
                    // Backup our progress
                    SaveState(i, fs.Position);

                    // Download the data
                    TsFileInfo tsInfo = pieces[i];
                    sw.Restart();
                    byte[] data = DownloadDataUberSafe(tsInfo.MakeUrl(hls_base));

                    // Write to file, log and do GUI features
                    totBytes /*+*/= data.Length;
                    Log("Writing " + data.Length.ToString() + " bytes to position " + fs.Position.ToString());
                    fs.Write(data, 0, data.Length);
                    AdvanceProgress();
                    SetSpeed(totBytes / 1024.0 / 1024.0 / sw.Elapsed.TotalSeconds);
                    Thread.Sleep(sleep);
                }
            }

            // Remove backup file since we finished
            File.Delete(StateFileName);

            // Encoding
            Log("FFMPEG");
            string mp4path = Path.Combine(outdir, id + ".mp4");
            if (File.Exists(mp4path))
            {
                new FileInfo(mp4path).Delete();
            }
            Process.Start("ffmpeg", "-i " + tspath + " -acodec copy -vcodec copy -bsf:a aac_adtstoasc " + mp4path);
            Log("Done.");
        }

        private int GetOffset(string x)
        {
            return int.Parse(x.Substring(x.LastIndexOf("=") + 1));
        }

        private void MakeState(string url, string path)
        {
            // Get general info about the playlist
            string hls_pl = Encoding.UTF8.GetString(DownloadDataSafe(url));
            string hls_base = url.Remove(url.LastIndexOf('/') + 1);
            List<TsFileInfo> pieces = GetPieces(hls_pl);

            // Run over the file
            long size = new FileInfo(path).Length;
            long cur_pos = 0;
            int i = 0;
            for (i = 0; i < pieces.Count; i++)
            {
                TsFileInfo piece = pieces[i];
                int cur_size = 1 + GetOffset(piece.end_offset) - GetOffset(piece.start_offset);
                if (cur_pos + cur_size <= size)
                {
                    cur_pos += cur_size;
                }
                else
                {
                    break;
                }
            }
            if (i == pieces.Count)
            {
                MessageBox.Show("File is complete");
            }
            else
            {
                File.WriteAllBytes(pieces[i - 1].name, DownloadDataUberSafe(pieces[i - 1].MakeUrl(hls_base)));
                SaveState(i, cur_pos);
                MessageBox.Show("Saved state");
            }
        }

        private List<TsFileInfo> GetPieces(string hls_pl)
        {
            List<TsFileInfo> pieces = new List<TsFileInfo>();

            TsFileInfo currInfo = new TsFileInfo() { name = null, size = 0 };
            foreach (string line in hls_pl.Split("\r\n".ToCharArray(), StringSplitOptions.RemoveEmptyEntries))
            {
                if (IsValidLine(line))
                {
                    TsFileInfo tsInfo = GetInfoFromLine(line);
                    if (tsInfo.name != currInfo.name)
                    {
                        // Starting a new TS file

                        // If we are not at the first entry, list the piece
                        if (currInfo.name != null)
                        {
                            pieces.Add(currInfo);
                        }

                        // Begin the new piece from our current line
                        currInfo = tsInfo;
                    }
                    else
                    {
                        // Continuing an existing piece, only update end offset
                        currInfo.end_offset = tsInfo.end_offset;
                        currInfo.size += tsInfo.size;
                    }
                }
            }
            pieces.Add(currInfo);
            return pieces;
        }

        Thread thread = null;
        private void button1_Click(object sender, EventArgs e)
        {
            thread = new Thread(new ThreadStart(delegate 
                {
                    string id = ParseID(vodId.Text);
                    string outDir = outputBox.Text;
                    string pl = GetVodPlaylist(id);
                    double ratio = double.Parse(ratioBox.Text);
                    double eratio = double.Parse(endRatioBox.Text);
                    DownloadPlaylist(pl, outDir, id, ratio, eratio);
                    
                }));
            thread.Start();
            button1.Enabled = false;
        }

        private void button2_Click(object sender, EventArgs e)
        {
            SaveFileDialog sfd = new SaveFileDialog() { Filter = "Text File|*.txt" };
            if (sfd.ShowDialog() != DialogResult.OK) 
                return;
            using (StreamWriter sw = new StreamWriter(File.Create(sfd.FileName)))
            {
                foreach (string item in logList.Items)
                {
                    sw.WriteLine(item);
                }
            }
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            outputBox.Text = Properties.Settings.Default.output;
            vodId.Text = Properties.Settings.Default.input;
        }

        private void outputBox_TextChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.output = outputBox.Text;
        }

        private void MainForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            Properties.Settings.Default.Save();
            Environment.Exit(0);
        }

        private void button3_Click(object sender, EventArgs e)
        {
            refresh = true;
        }

        private void button4_Click(object sender, EventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog() { Filter = "TS Files (*.ts)|*.ts" };
            if (ofd.ShowDialog() != DialogResult.OK)
                return;
            thread = new Thread(new ThreadStart(delegate
                {
                    string id = ParseID(vodId.Text);
                    string pl = GetVodPlaylist(id);
                    MakeState(pl, ofd.FileName);
                }));
            thread.Start();
        }

        private void vodId_TextChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.input = vodId.Text;
        }

        private int sleep = 0;

        private void sleepSetBtn_Click(object sender, EventArgs e)
        {
            if (!int.TryParse(sleepBox.Text, out sleep))
            {
                MessageBox.Show("Error", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    public struct TsFileInfo
    {
        public string name;
        public string start_offset;
        public string end_offset;
        public long size;

        public string MakeUrl(string hls_base)
        {
            return hls_base + name + "?" + start_offset + "&" + end_offset;
        }
    }

    public struct AuthData
    {
        public string token;
        public string sig;
    }

    public class RefreshException : Exception
    {

    }
}
