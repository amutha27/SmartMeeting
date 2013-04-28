using Microsoft.Kinect;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace USC.Robotics.SmartMeeting
{
    internal static class Global
    {
        public static readonly Dictionary<Skeleton, string> trackedPeople = new Dictionary<Skeleton,string>();
        public static System.Windows.Controls.TextBlock StatusBarText;
        public static string personSpeaking;
        public static System.IO.Stream audioStream;
        public static readonly int positionError = 8;
    }
}
