using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RustServerManager
{
    class MessageSchedule
    {
        public int message_schedule_type;

        public int message_hour;
        public int message_minute;

        public string message;

        //For schedule type 0
        public int xdays;
        public DateTime last_run;

        //For schedule type 1 and 3
        public int weekday;

        //For schedule type 2
        public int monthday;

        //For schedule type 3
        public bool first_weekday_of_month;
        public bool second_weekday_of_month;
        public bool third_weekday_of_month;
        public bool last_weekday_of_month;

    }
}
