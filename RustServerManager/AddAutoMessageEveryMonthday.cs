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
    public partial class AddAutoMessageEveryMonthday : Form
    {
        Form1 parent;

        public AddAutoMessageEveryMonthday(Form1 parent)
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
            parent.CreateMessageEveryMonthday(monthday, int.Parse("" + numericUpDown2.Value), int.Parse("" + numericUpDown3.Value), textBox1.Text);
            this.Close();
        }
    }
}
