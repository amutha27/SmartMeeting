using Microsoft.Kinect;
using Microsoft.Kinect.Toolkit;
using Microsoft.Speech.Recognition;
using System;
using System.Collections.Generic;
using System.IO;
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
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {

        private static readonly int Bgr32BytesPerPixel = (PixelFormats.Bgr32.BitsPerPixel + 7) / 8;
        private readonly KinectSensorChooser sensorChooser = new KinectSensorChooser();
        private WriteableBitmap colorImageWritableBitmap;
        private byte[] colorImageData;
        private ColorImageFormat currentColorImageFormat = ColorImageFormat.Undefined;
        private Stream audioStream;
        private KinectSensor kinect;

        /// <summary>
        /// Name of speech grammar corresponding to speech acceptable during welcome screen.
        /// </summary>
        private const string WelcomeSpeechRule = "welcomeRule";

        /// <summary>
        /// Name of speech grammar corresponding to speech acceptable during game play.
        /// </summary>
        private const string GameSpeechRule = "gameRule";

        /// <summary>
        /// Name of speech grammar corresponding to speech acceptable during game end experience.
        /// </summary>
        private const string GameEndSpeechRule = "gameEndRule";

        /// <summary>
        /// Speech recognizer used to detect voice commands issued by application users.
        /// </summary>
        private SpeechRecognizer speechRecognizer;

        /// <summary>
        /// Speech grammar used during welcome screen.
        /// </summary>
        private Grammar welcomeGrammar;

        /// <summary>
        /// Speech grammar used during game play.
        /// </summary>
        private Grammar gameGrammar;

        /// <summary>
        /// Speech grammar used after a game ends, to control starting a new one.
        /// </summary>
        private Grammar gameEndGrammar;

        /// <summary>
        /// true if placement rules are being enforced. false if players can play out of turn and in any board space.
        /// </summary>
        private bool rulesEnabled = true;

        /// <summary>
        /// Create a grammar from grammar definition XML file.
        /// </summary>
        /// <param name="ruleName">
        /// Rule corresponding to grammar we want to use.
        /// </param>
        /// <returns>
        /// New grammar object corresponding to specified rule.
        /// </returns>
        private Grammar CreateGrammar(string ruleName)
        {
            Grammar grammar;

            using (var memoryStream = new MemoryStream(System.Text.Encoding.ASCII.GetBytes(Properties.Resources.SpeechGrammar)))
            {
                grammar = new Grammar(memoryStream, ruleName);
            }

            return grammar;
        }

        /// <summary>
        /// Enable specified grammar and disable all others.
        /// </summary>
        /// <param name="grammar">
        /// Grammar to be enabled. May be null to disable all grammars.
        /// </param>
        private void EnableGrammar(Grammar grammar)
        {
            if (null != this.gameGrammar)
            {
                this.gameGrammar.Enabled = grammar == this.gameGrammar;
            }

            if (null != this.welcomeGrammar)
            {
                this.welcomeGrammar.Enabled = grammar == this.welcomeGrammar;
            }

            if (null != this.gameEndGrammar)
            {
                this.gameEndGrammar.Enabled = grammar == this.gameEndGrammar;
            }
        }
       

        public MainWindow()
        {
            InitializeComponent();

            var faceTagViewerBinding = new Binding("Kinect") { Source = sensorChooser };
            this.faceTagViewer.SetBinding(FaceTagViewer.KinectProperty, faceTagViewerBinding);

            var audioVisualizerBinding = new Binding("Kinect") { Source = sensorChooser };
            this.audioVisualizer.SetBinding(AudioVisualizer.KinectProperty, audioVisualizerBinding);

            var speechTagVieweBinding = new Binding("Kinect") { Source = sensorChooser };
            this.speechTagViewer.SetBinding(SpeechTagViewer.KinectProperty, speechTagVieweBinding);

            sensorChooser.KinectChanged += SensorChooserOnKinectChanged;

            sensorChooser.Start();
        }

        private void SensorChooserOnKinectChanged(object sender, KinectChangedEventArgs kinectChangedEventArgs)
        {
            KinectSensor oldSensor = kinectChangedEventArgs.OldSensor;
            KinectSensor newSensor = kinectChangedEventArgs.NewSensor;

            if (oldSensor != null)
            {
                oldSensor.AllFramesReady -= KinectSensorOnAllFramesReady;
                oldSensor.ColorStream.Disable();
                oldSensor.DepthStream.Disable();
                oldSensor.DepthStream.Range = DepthRange.Default;
                oldSensor.SkeletonStream.Disable();
                oldSensor.SkeletonStream.AppChoosesSkeletons = false;
                oldSensor.SkeletonStream.EnableTrackingInNearRange = false;
                oldSensor.SkeletonStream.TrackingMode = SkeletonTrackingMode.Default;
            }

            if (newSensor != null)
            {
                try
                {
                    newSensor.ColorStream.Enable(ColorImageFormat.RgbResolution1280x960Fps12);
                    newSensor.DepthStream.Enable(DepthImageFormat.Resolution80x60Fps30);
                    newSensor.DepthStream.Range = DepthRange.Default;
                    newSensor.SkeletonStream.EnableTrackingInNearRange = false;
                    newSensor.SkeletonStream.TrackingMode = SkeletonTrackingMode.Seated;
                    newSensor.AudioSource.BeamAngleMode = BeamAngleMode.Adaptive;
                    newSensor.SkeletonStream.Enable();
                    kinect = newSensor;
                    //newSensor.SkeletonStream.AppChoosesSkeletons = true;
                    newSensor.AllFramesReady += KinectSensorOnAllFramesReady;
                }
                catch (InvalidOperationException)
                {
                    // This exception can be thrown when we are trying to
                    // enable streams on a device that has gone away.  This
                    // can occur, say, in app shutdown scenarios when the sensor
                    // goes away between the time it changed status and the
                    // time we get the sensor changed notification.
                    //
                    // Behavior here is to just eat the exception and assume
                    // another notification will come along if a sensor
                    // comes back.
                }
            }
        }

        private void KinectSensorOnAllFramesReady(object sender, AllFramesReadyEventArgs allFramesReadyEventArgs)
        {
            using (var colorImageFrame = allFramesReadyEventArgs.OpenColorImageFrame())
            {
                if (colorImageFrame == null)
                {
                    return;
                }

                // Make a copy of the color frame for displaying.
                var haveNewFormat = this.currentColorImageFormat != colorImageFrame.Format;
                if (haveNewFormat)
                {
                    this.currentColorImageFormat = colorImageFrame.Format;
                    this.colorImageData = new byte[colorImageFrame.PixelDataLength];
                    this.colorImageWritableBitmap = new WriteableBitmap(
                        colorImageFrame.Width, colorImageFrame.Height, 96, 96, PixelFormats.Bgr32, null);
                    ColorImage.Source = this.colorImageWritableBitmap;
                }

                colorImageFrame.CopyPixelDataTo(this.colorImageData);
                this.colorImageWritableBitmap.WritePixels(
                    new Int32Rect(0, 0, colorImageFrame.Width, colorImageFrame.Height),
                    this.colorImageData,
                    colorImageFrame.Width * Bgr32BytesPerPixel,
                    0);
            }
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            faceTagViewer.Dispose();
            audioVisualizer.Dispose();
            sensorChooser.Stop();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Create and configure speech grammars and recognizer
            this.welcomeGrammar = CreateGrammar(WelcomeSpeechRule);
            this.gameGrammar = CreateGrammar(GameSpeechRule);
            this.gameEndGrammar = CreateGrammar(GameEndSpeechRule);
            this.EnableGrammar(this.welcomeGrammar);
            this.speechRecognizer = SpeechRecognizer.Create(new[] { welcomeGrammar, gameGrammar, gameEndGrammar });


            if (null != speechRecognizer)
            {
                this.speechRecognizer.SpeechRecognized += SpeechRecognized;
                Global.audioStream = this.speechRecognizer.Start(kinect.AudioSource);
            }
        }
        /// <summary>
        /// Handles speech recognition events.
        /// </summary>
        /// <param name="sender">
        /// Object sending the event.
        /// </param>
        /// <param name="e">
        /// Event arguments.
        /// </param>
        private void SpeechRecognized(object sender, SpeechRecognizerEventArgs e)
        {
            txtTranscript.Text += Global.personSpeaking+": " + e.Phrase+System.Environment.NewLine;
            txtTranscript.ScrollToEnd();
        }


    }
}
