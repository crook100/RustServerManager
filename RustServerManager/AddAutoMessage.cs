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
    public partial class AddAutoMessage : Form
    {
        Form1 parent;
        
        public AddAutoMessage(Form1 parent)
        {
            this.parent = parent;
            InitializeComponent();
        }

        private void AddWipeSchedule_Load(object sender, EventArgs e)
        {

        }

        private void button2_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private void button1_Click(object sender, EventArgs e)
        {
            if (radioButton7.Checked)
            {
                //Message every X days
                AddAutoMessageEveryXDays dialog = new AddAutoMessageEveryXDays(parent);
                dialog.ShowDialog();
                this.Close();
            }

            if (radioButton1.Checked)
            {
                //Message by weekday
                AddAutoMessageEveryWeekday dialog = new AddAutoMessageEveryWeekday(parent);
                dialog.ShowDialog();
                this.Close();
            }

            if (radioButton2.Checked)
            {
                //Wipe by month day
                AddAutoMessageEveryMonthday dialog = new AddAutoMessageEveryMonthday(parent);
                dialog.ShowDialog();
                this.Close();
            }

            if (radioButton3.Checked)
            {
                //Wipe by first, second, third or last weekday in month
                AddAutoMessageEvery1st2nd3rd dialog = new AddAutoMessageEvery1st2nd3rd(parent);
                dialog.ShowDialog();
                this.Close();
            }
        }
    }
}
