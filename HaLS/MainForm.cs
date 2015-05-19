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
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace HaLS
{
    public partial class MainForm : Form
    {
        public MainForm()
        {
            InitializeComponent();
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
                    WebRequest req = HttpWebRequest.Create(url);
                    using (WebResponse resp = req.GetResponse())
                    {
                        long len = resp.ContentLength;
                        byte[] buf = new byte[len];
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
                        }
                        return buf;
                    }
                }
                catch (Exception e)
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

        private void DownloadVod(string id, string outdir)
        {
            string authdata = Encoding.UTF8.GetString(DownloadDataSafe("https://api.twitch.tv/api/vods/" + id + "/access_token"));
            AuthData authdata_obj = JsonConvert.DeserializeObject<AuthData>(authdata);
            string pl_opts = Encoding.UTF8.GetString(DownloadDataSafe("http://usher.justin.tv/vod/" + id + "?nauth=" + authdata_obj.token + "&nauthsig=" + authdata_obj.sig));
            List<string> opts = pl_opts.Split("\r\n".ToCharArray(), StringSplitOptions.RemoveEmptyEntries).Where(x => IsValidLine(x)).ToList();
            QualitySelect qs = new QualitySelect(opts);
            qs.ShowDialog();
            DownloadPlaylist(qs.selection, outdir, id);
        }

        private void DownloadPlaylist(string url, string outdir, string id)
        {
            string hls_pl = Encoding.UTF8.GetString(DownloadDataSafe(url));
            string hls_base = url.Remove(url.LastIndexOf('/') + 1);

            List<TsFileInfo> pieces = new List<TsFileInfo>();

            TsFileInfo currInfo = new TsFileInfo() { name = null };
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
                    }
                }
            }
            pieces.Add(currInfo);

            InitProgress(pieces.Count);

            Task task = null;
            Stopwatch sw = new Stopwatch();
            sw.Start();
            long totBytes = 0;

            string tspath = Path.Combine(outdir, id + ".ts");
            // We now have all pieces and need to just download and concat them
            using (FileStream fs = File.Create(tspath))
            {
                foreach (TsFileInfo tsInfo in pieces)
                {
                    string cur_url = hls_base + tsInfo.name + "?" + tsInfo.start_offset + "&" + tsInfo.end_offset;
                    byte[] data = DownloadDataSafe(cur_url);

                    if (debug)
                    {
                        WriteAllBytesAsync(Path.Combine(outdir, tsInfo.name), data);
                    }

                    totBytes += data.Length;
                    if (task != null)
                    {
                        task.Wait();
                    }
                    Log("Writing " + data.Length.ToString() + " bytes to position " + fs.Position.ToString());
                    task = fs.WriteAsync(data, 0, data.Length);

                    AdvanceProgress();
                    SetSpeed(totBytes / 1024.0 / 1024.0 / sw.Elapsed.TotalSeconds);
                }
            }

            Log("FFMPEG");
            string mp4path = Path.Combine(outdir, id + ".mp4");
            if (File.Exists(mp4path))
            {
                new FileInfo(mp4path).Delete();
            }
            Process.Start("ffmpeg", "-i " + tspath + " -acodec copy -vcodec copy -bsf:a aac_adtstoasc " + mp4path);
            Log("Done.");
        }

        Thread thread = null;
        private void button1_Click(object sender, EventArgs e)
        {
            thread = new Thread(new ThreadStart(delegate { DownloadVod(vodId.Text, outputBox.Text); }));
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
        }

        private void outputBox_TextChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.output = outputBox.Text;
        }

        private void MainForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            Properties.Settings.Default.Save();
        }
    }

    public struct TsFileInfo
    {
        public string name;
        public string start_offset;
        public string end_offset;
    }

    public struct AuthData
    {
        public string token;
        public string sig;
    }

}
