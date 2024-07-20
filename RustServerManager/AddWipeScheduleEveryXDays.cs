using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RustServerManager
{
    public partial class AddWipeScheduleEveryXDays : Form
    {
        Form1 parent;

        public AddWipeScheduleEveryXDays(Form1 parent)
        {
            this.parent = parent;
            InitializeComponent();
        }

        private void button2_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private void button1_Click(object sender, EventArgs e)
        {
            if (radioButton4.Checked)
            {
                parent.CreateWipeEveryXDays(int.Parse("" + numericUpDown1.Value), int.Parse("" + numericUpDown2.Value), int.Parse("" + numericUpDown3.Value), 0);
            }
            if (radioButton5.Checked)
            {
                parent.CreateWipeEveryXDays(int.Parse("" + numericUpDown1.Value), int.Parse("" + numericUpDown2.Value), int.Parse("" + numericUpDown3.Value), 1);
            }
            if (radioButton6.Checked)
            {
                parent.CreateWipeEveryXDays(int.Parse("" + numericUpDown1.Value), int.Parse("" + numericUpDown2.Value), int.Parse("" + numericUpDown3.Value), 2);
            }
            this.Close();
        }
    }
}
