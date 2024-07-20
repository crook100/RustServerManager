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
    public partial class AddAutoMessageEveryWeekday : Form
    {
        Form1 parent;

        public AddAutoMessageEveryWeekday(Form1 parent)
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
            parent.CreateMessageEveryWeekday(weekday, int.Parse("" + numericUpDown2.Value), int.Parse("" + numericUpDown3.Value), textBox1.Text);
            this.Close();
        }

        private void AddWipeScheduleEveryWeekday_Load(object sender, EventArgs e)
        {
            comboBox1.SelectedIndex = 4;
        }
    }
}
