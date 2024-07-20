using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RustServerManager
{
    class WipeSchedule
    {
        /*0 for map
          1 for blueprint
          2 for full
        */
        public int wipe_type;

        /*0 for every x days
          1 for weekday
          2 for monthday
          3 for every 1st, 2nd, 3rd or last weekday of month
        */
        public int wipe_schedule_type;

        public int wipe_hour;
        public int wipe_minute;

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
