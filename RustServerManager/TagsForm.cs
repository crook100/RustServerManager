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
    public partial class TagsForm : Form
    {
        Form1 parent;
        string old_tags;

        public TagsForm(Form1 parent, string old_tags)
        {
            this.parent = parent;
            this.old_tags = old_tags;
            InitializeComponent();
        }

        private void checkedListBox1_ItemCheck(object sender, ItemCheckEventArgs e)
        {
            if (e.NewValue == CheckState.Checked && checkedListBox1.CheckedItems.Count >= 3) 
            {
                e.NewValue = CheckState.Unchecked;
                MessageBox.Show("Up to 3 tags are allowed per-server. Please uncheck another tag and try again.");
            }
        }

        private void button1_Click(object sender, EventArgs e)
        {
            string tags = "";
            foreach(string clb in checkedListBox1.CheckedItems) 
            {
                tags += clb.Substring(0, clb.IndexOf(']')).Replace("]", "").Replace("[", "")  + ",";
            }
            tags = tags.Substring(0, tags.Length-1);
            parent.SaveTags(tags);
            this.Close();
        }

        private void TagsForm_Load(object sender, EventArgs e)
        {
            foreach(string tag in old_tags.Split(',')) 
            {
                for (int i = 0; i < checkedListBox1.Items.Count; i++) 
                {
                    if (checkedListBox1.Items[i].ToString().StartsWith("[" + tag + "]")) 
                    {
                        checkedListBox1.SetItemChecked(i, true);
                    }
                }
            }
        }
    }
}
