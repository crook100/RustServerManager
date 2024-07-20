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
    public partial class AddWipeScheduleEveryMonthday : Form
    {
        Form1 parent;

        public AddWipeScheduleEveryMonthday(Form1 parent)
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
            int monthday = int.Parse("" + numericUpDown1.Value);
            if (radioButton4.Checked)
            {
                parent.CreateWipeEveryMonthday(monthday, int.Parse("" + numericUpDown2.Value), int.Parse("" + numericUpDown3.Value), 0);
            }
            if (radioButton5.Checked)
            {
                parent.CreateWipeEveryMonthday(monthday, int.Parse("" + numericUpDown2.Value), int.Parse("" + numericUpDown3.Value), 1);
            }
            if (radioButton6.Checked)
            {
                parent.CreateWipeEveryMonthday(monthday, int.Parse("" + numericUpDown2.Value), int.Parse("" + numericUpDown3.Value), 2);
            }
            this.Close();
        }
    }
}
