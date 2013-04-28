using Microsoft.Kinect;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace USC.Robotics.SmartMeeting
{
    /// <summary>
    /// Interaction logic for SpeechTagViewer.xaml
    /// </summary>
    public partial class SpeechTagViewer : UserControl
    {
        public static readonly DependencyProperty KinectProperty = DependencyProperty.Register(
           "Kinect",
           typeof(KinectSensor),
           typeof(SpeechTagViewer),
           new PropertyMetadata(
               null, (o, args) => ((SpeechTagViewer)o).OnSensorChanged((KinectSensor)args.OldValue, (KinectSensor)args.NewValue)));

        public KinectSensor Kinect
        {
            get
            {
                return (KinectSensor)this.GetValue(KinectProperty);
            }

            set
            {
                this.SetValue(KinectProperty, value);
            }
        }
        RotateTransform beamRotation;
        RotateTransform sourceRotation;

        public SpeechTagViewer()
        {
            InitializeComponent();
            this.beamRotation = new RotateTransform();
            this.sourceRotation = new RotateTransform();
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            //This transform is applied as the target image is scaled
            drawingContext.PushTransform(new ScaleTransform(0.25, 0.25));

            base.OnRender(drawingContext);
            foreach (Skeleton skeleton in Global.trackedPeople.Keys)
            {
                SkeletonPoint point = skeleton.Position;
                double skeletonPos = ((Math.Atan(point.X / point.Z))*180/Math.PI);
                if (Math.Abs(this.sourceRotation.Angle + skeletonPos) <= Global.positionError)
                {
                    drawingContext.DrawText(
                        new FormattedText(Global.trackedPeople[skeleton] + "..Speaking", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Verdana"), 70, Brushes.Blue),
                        new Point(0,0));
                    Global.personSpeaking = Global.trackedPeople[skeleton];
                }
            }
        }

        private void OnAllFramesReady(object sender, AllFramesReadyEventArgs allFramesReadyEventArgs)
        {
                this.InvalidateVisual();
        }

        private void OnSensorChanged(KinectSensor oldSensor, KinectSensor newSensor)
        {
            if (oldSensor != null)
            {
                oldSensor.AllFramesReady -= this.OnAllFramesReady;
                oldSensor.AudioSource.BeamAngleChanged -= this.AudioSourceBeamChanged;
                oldSensor.AudioSource.SoundSourceAngleChanged -= this.AudioSourceSoundSourceAngleChanged;
            }

            if (newSensor != null)
            {
                newSensor.AudioSource.BeamAngleChanged += this.AudioSourceBeamChanged;
                newSensor.AudioSource.SoundSourceAngleChanged += this.AudioSourceSoundSourceAngleChanged;
                newSensor.AllFramesReady += this.OnAllFramesReady;
            }
        }

        /// <summary>
        /// Handles event triggered when audio beam angle changes.
        /// </summary>
        /// <param name="sender">object sending the event.</param>
        /// <param name="e">event arguments.</param>
        private void AudioSourceBeamChanged(object sender, BeamAngleChangedEventArgs e)
        {
            beamRotation.Angle = -e.Angle;

        }

        /// <summary>
        /// Handles event triggered when sound source angle changes.
        /// </summary>
        /// <param name="sender">object sending the event.</param>
        /// <param name="e">event arguments.</param>
        private void AudioSourceSoundSourceAngleChanged(object sender, SoundSourceAngleChangedEventArgs e)
        {
             //Rotate gradient to match angle
            if (this.sourceRotation.Angle != -e.Angle && e.ConfidenceLevel>0.9)
            {
                sourceRotation.Angle = -e.Angle;
                beamRotation.Angle = sourceRotation.Angle;
            }


        }

    }
}
