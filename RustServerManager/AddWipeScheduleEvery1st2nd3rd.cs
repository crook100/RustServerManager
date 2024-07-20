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
    public partial class AddWipeScheduleEvery1st2nd3rd : Form
    {
        Form1 parent;

        public AddWipeScheduleEvery1st2nd3rd(Form1 parent)
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
            int weekday = comboBox1.SelectedIndex;

            bool first = radioButton1.Checked;
            bool second = radioButton2.Checked;
            bool third = radioButton3.Checked;
            bool last = radioButton7.Checked;

            if (radioButton4.Checked)
            {
                parent.CreateWipeEvery1st2nd3rd(weekday, first, second, third, last, int.Parse("" + numericUpDown2.Value), int.Parse("" + numericUpDown3.Value), 0);
            }
            if (radioButton5.Checked)
            {
                parent.CreateWipeEvery1st2nd3rd(weekday, first, second, third, last, int.Parse("" + numericUpDown2.Value), int.Parse("" + numericUpDown3.Value), 1);
            }
            if (radioButton6.Checked)
            {
                parent.CreateWipeEvery1st2nd3rd(weekday, first, second, third, last, int.Parse("" + numericUpDown2.Value), int.Parse("" + numericUpDown3.Value), 2);
            }
            this.Close();
        }

        private void AddWipeScheduleEvery1st2nd3rd_Load(object sender, EventArgs e)
        {
            comboBox1.SelectedIndex = 4;
        }
    }
}
