/* Copyright (C) 2015 haha01haha01

* This Source Code Form is subject to the terms of the Mozilla Public
* License, v. 2.0. If a copy of the MPL was not distributed with this
* file, You can obtain one at http://mozilla.org/MPL/2.0/. */

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace HaLS
{
    public partial class QualitySelect : Form
    {
        private bool closing = false;
        private List<string> opts = null;
        public string selection = null;

        public QualitySelect(List<string> opts)
        {
            InitializeComponent();
            this.opts = opts;
            this.opts.ForEach(x => optionsBox.Items.Add(x));
            optionsBox.SelectedIndex = 0;
        }

        private void button1_Click(object sender, EventArgs e)
        {
            selection = opts[optionsBox.SelectedIndex];
            closing = true;
            Close();
        }

        private void QualitySelect_FormClosing(object sender, FormClosingEventArgs e)
        {
            e.Cancel = !closing;
        }
    }
}
