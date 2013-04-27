using Microsoft.Kinect;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
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
    /// Interaction logic for AudioVisualizer.xaml
    /// </summary>
    public partial class AudioVisualizer : UserControl
    {
        /// <summary>
        /// Keeps track of the kinect sensor passed to this class
        /// </summary>
        public static readonly DependencyProperty KinectProperty = DependencyProperty.Register(
            "Kinect",
            typeof(KinectSensor),
            typeof(AudioVisualizer),
            new PropertyMetadata(
                null, (o, args) => ((AudioVisualizer)o).OnSensorChanged((KinectSensor)args.OldValue, (KinectSensor)args.NewValue)));
        
        /// <summary>
        /// Number of milliseconds between each read of audio data from the stream.
        /// Faster polling (few tens of ms) ensures a smoother audio stream visualization.
        /// </summary>
        private const int AudioPollingInterval = 100;

        /// <summary>
        /// Number of samples captured from Kinect audio stream each millisecond.
        /// </summary>
        private const int SamplesPerMillisecond = 16;

        /// <summary>
        /// Number of bytes in each Kinect audio stream sample.
        /// </summary>
        private const int BytesPerSample = 2;

        /// <summary>
        /// Number of audio samples represented by each column of pixels in wave bitmap.
        /// </summary>
        private const int SamplesPerColumn = 40;

        /// <summary>
        /// Width of bitmap that stores audio stream energy data ready for visualization.
        /// </summary>
        private const int EnergyBitmapWidth = 780;

        /// <summary>
        /// Height of bitmap that stores audio stream energy data ready for visualization.
        /// </summary>
        private const int EnergyBitmapHeight = 195;

        /// <summary>
        /// Bitmap that contains constructed visualization for audio stream energy, ready to
        /// be displayed. It is a 2-color bitmap with white as background color and blue as
        /// foreground color.
        /// </summary>
        private readonly WriteableBitmap energyBitmap;

        /// <summary>
        /// Rectangle representing the entire energy bitmap area. Used when drawing background
        /// for energy visualization.
        /// </summary>
        private readonly Int32Rect fullEnergyRect = new Int32Rect(0, 0, EnergyBitmapWidth, EnergyBitmapHeight);

        /// <summary>
        /// Array of background-color pixels corresponding to an area equal to the size of whole energy bitmap.
        /// </summary>
        private readonly byte[] backgroundPixels = new byte[EnergyBitmapWidth * EnergyBitmapHeight];

        /// <summary>
        /// Buffer used to hold audio data read from audio stream.
        /// </summary>
        private readonly byte[] audioBuffer = new byte[AudioPollingInterval * SamplesPerMillisecond * BytesPerSample];

        /// <summary>
        /// Buffer used to store audio stream energy data as we read audio.
        /// 
        /// We store 25% more energy values than we strictly need for visualization to allow for a smoother
        /// stream animation effect, since rendering happens on a different schedule with respect to audio
        /// capture.
        /// </summary>
        private readonly double[] energy = new double[(uint)(EnergyBitmapWidth * 1.25)];

        /// <summary>
        /// Object for locking energy buffer to synchronize threads.
        /// </summary>
        private readonly object energyLock = new object();

        /// <summary>
        /// Active Kinect sensor.
        /// </summary>
        private KinectSensor sensor
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

        /// <summary>
        /// <code>true</code> if audio is currently being read from Kinect stream, <code>false</code> otherwise.
        /// </summary>
        private bool reading;

        /// <summary>
        /// Thread that is reading audio from Kinect stream.
        /// </summary>
        private Thread readingThread;

        /// <summary>
        /// Array of foreground-color pixels corresponding to a line as long as the energy bitmap is tall.
        /// This gets re-used while constructing the energy visualization.
        /// </summary>
        private byte[] foregroundPixels;

        /// <summary>
        /// Sum of squares of audio samples being accumulated to compute the next energy value.
        /// </summary>
        private double accumulatedSquareSum;

        /// <summary>
        /// Number of audio samples accumulated so far to compute the next energy value.
        /// </summary>
        private int accumulatedSampleCount;

        /// <summary>
        /// Index of next element available in audio energy buffer.
        /// </summary>
        private int energyIndex;

        /// <summary>
        /// Number of newly calculated audio stream energy values that have not yet been
        /// displayed.
        /// </summary>
        private int newEnergyAvailable;

        /// <summary>
        /// Error between time slice we wanted to display and time slice that we ended up
        /// displaying, given that we have to display in integer pixels.
        /// </summary>
        private double energyError;

        /// <summary>
        /// Last time energy visualization was rendered to screen.
        /// </summary>
        private DateTime? lastEnergyRefreshTime;

        /// <summary>
        /// Index of first energy element that has never (yet) been displayed to screen.
        /// </summary>
        private int energyRefreshIndex;

        /// <summary>
        /// Sets up the visualizer and starts the audio reading thread
        /// </summary>
        public AudioVisualizer()
        {
            InitializeComponent();

            this.energyBitmap = new WriteableBitmap(EnergyBitmapWidth, EnergyBitmapHeight, 96, 96, PixelFormats.Indexed1, new BitmapPalette(new List<Color> { Colors.White, (Color)this.Resources["KinectPurpleColor"] }));

            // Initialize foreground pixels
            this.foregroundPixels = new byte[EnergyBitmapHeight];
            for (int i = 0; i < this.foregroundPixels.Length; ++i)
            {
                this.foregroundPixels[i] = 0xff;
            }

            this.waveDisplay.Source = this.energyBitmap;
        }

        /// <summary>
        /// Calls the dispose method
        /// </summary>
        ~AudioVisualizer()
        {
            this.Dispose();
        }

        /// <summary>
        /// Stops the audio reading thread
        /// </summary>
        public void Dispose()
        {
            // Tell audio reading thread to stop and wait for it to finish.
            this.reading = false;
            if (null != readingThread)
            {
                readingThread.Join();
            }
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Handles event triggered when a kinect sensor is changed
        /// </summary>
        /// <param name="oldSensor">old kinect sesnor</param>
        /// <param name="newSensor">new kinect sensor</param>
        private void OnSensorChanged(KinectSensor oldSensor, KinectSensor newSensor)
        {
            if (oldSensor != null)
            {
                // Tell audio reading thread to stop and wait for it to finish.
                this.reading = false;
                if (null != readingThread)
                {
                    readingThread.Join();
                }

                CompositionTarget.Rendering -= UpdateEnergy;

                oldSensor.AudioSource.BeamAngleChanged -= this.AudioSourceBeamChanged;
                oldSensor.AudioSource.SoundSourceAngleChanged -= this.AudioSourceSoundSourceAngleChanged;
                oldSensor.AudioSource.BeamAngleMode = BeamAngleMode.Automatic;

                oldSensor.AudioSource.Stop();
            }

            if (newSensor != null)
            {
                CompositionTarget.Rendering += UpdateEnergy;

                newSensor.AudioSource.BeamAngleChanged += this.AudioSourceBeamChanged;
                newSensor.AudioSource.SoundSourceAngleChanged += this.AudioSourceSoundSourceAngleChanged;
                newSensor.AudioSource.BeamAngleMode = BeamAngleMode.Adaptive;


                

                // Use a separate thread for capturing audio because audio stream read operations
                // will block, and we don't want to block main UI thread.
                this.reading = true;
                this.readingThread = new Thread(AudioReadingThread);
                this.readingThread.Start();
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

            beamAngleText.Text = string.Format(CultureInfo.CurrentCulture, Properties.Resources.BeamAngle, e.Angle.ToString("0", CultureInfo.CurrentCulture));
        }

        /// <summary>
        /// Handles event triggered when sound source angle changes.
        /// </summary>
        /// <param name="sender">object sending the event.</param>
        /// <param name="e">event arguments.</param>
        private void AudioSourceSoundSourceAngleChanged(object sender, SoundSourceAngleChangedEventArgs e)
        {
            // Maximum possible confidence corresponds to this gradient width
            const double MinGradientWidth = 0.04;

            // Set width of mark based on confidence.
            // A confidence of 0 would give us a gradient that fills whole area diffusely.
            // A confidence of 1 would give us the narrowest allowed gradient width.
            double halfWidth = Math.Max((1 - e.ConfidenceLevel), MinGradientWidth) / 2;

            // Update the gradient representing sound source position to reflect confidence
            this.sourceGsPre.Offset = Math.Max(this.sourceGsMain.Offset - halfWidth, 0);
            this.sourceGsPost.Offset = Math.Min(this.sourceGsMain.Offset + halfWidth, 1);

            // Rotate gradient to match angle
            sourceRotation.Angle = -e.Angle;

            sourceAngleText.Text = string.Format(CultureInfo.CurrentCulture, Properties.Resources.SourceAngle, e.Angle.ToString("0", CultureInfo.CurrentCulture));
            sourceConfidenceText.Text = string.Format(CultureInfo.CurrentCulture, Properties.Resources.SourceConfidence, e.ConfidenceLevel.ToString("0.00", CultureInfo.CurrentCulture));
        }

        /// <summary>
        /// Handles rendering energy visualization into a bitmap.
        /// </summary>
        /// <param name="sender">object sending the event.</param>
        /// <param name="e">event arguments.</param>
        private void UpdateEnergy(object sender, EventArgs e)
        {
            lock (this.energyLock)
            {
                // Calculate how many energy samples we need to advance since the last update in order to
                // have a smooth animation effect
                DateTime now = DateTime.UtcNow;
                DateTime? previousRefreshTime = this.lastEnergyRefreshTime;
                this.lastEnergyRefreshTime = now;

                // No need to refresh if there is no new energy available to render
                if (this.newEnergyAvailable <= 0)
                {
                    return;
                }

                if (previousRefreshTime != null)
                {
                    double energyToAdvance = this.energyError + (((now - previousRefreshTime.Value).TotalMilliseconds * SamplesPerMillisecond) / SamplesPerColumn);
                    int energySamplesToAdvance = Math.Min(this.newEnergyAvailable, (int)Math.Round(energyToAdvance));
                    this.energyError = energyToAdvance - energySamplesToAdvance;
                    this.energyRefreshIndex = (this.energyRefreshIndex + energySamplesToAdvance) % this.energy.Length;
                    this.newEnergyAvailable -= energySamplesToAdvance;
                }

                // clear background of energy visualization area
                this.energyBitmap.WritePixels(fullEnergyRect, this.backgroundPixels, EnergyBitmapWidth, 0);

                // Draw each energy sample as a centered vertical bar, where the length of each bar is
                // proportional to the amount of energy it represents.
                // Time advances from left to right, with current time represented by the rightmost bar.
                int baseIndex = (this.energyRefreshIndex + this.energy.Length - EnergyBitmapWidth) % this.energy.Length;
                for (int i = 0; i < EnergyBitmapWidth; ++i)
                {
                    const int HalfImageHeight = EnergyBitmapHeight / 2;

                    // Each bar has a minimum height of 1 (to get a steady signal down the middle) and a maximum height
                    // equal to the bitmap height.
                    int barHeight = (int)Math.Max(1.0, (this.energy[(baseIndex + i) % this.energy.Length] * EnergyBitmapHeight));

                    // Center bar vertically on image
                    var barRect = new Int32Rect(i, HalfImageHeight - (barHeight / 2), 1, barHeight);

                    // Draw bar in foreground color
                    this.energyBitmap.WritePixels(barRect, foregroundPixels, 1, 0);
                }
            }
        }

        /// <summary>
        /// Handles polling audio stream and updating visualization every tick.
        /// </summary>
        private void AudioReadingThread()
        {
            // Bottom portion of computed energy signal that will be discarded as noise.
            // Only portion of signal above noise floor will be displayed.
            const double EnergyNoiseFloor = 0.2;

            while (this.reading)
            {
                if (Global.audioStream != null)
                {

                    int readCount = Global.audioStream.Read(audioBuffer, 0, audioBuffer.Length);

                    // Calculate energy corresponding to captured audio.
                    // In a computationally intensive application, do all the processing like
                    // computing energy, filtering, etc. in a separate thread.
                    lock (this.energyLock)
                    {
                        for (int i = 0; i < readCount; i += 2)
                        {
                            // compute the sum of squares of audio samples that will get accumulated
                            // into a single energy value.
                            short audioSample = BitConverter.ToInt16(audioBuffer, i);
                            this.accumulatedSquareSum += audioSample * audioSample;
                            ++this.accumulatedSampleCount;

                            if (this.accumulatedSampleCount < SamplesPerColumn)
                            {
                                continue;
                            }

                            // Each energy value will represent the logarithm of the mean of the
                            // sum of squares of a group of audio samples.
                            double meanSquare = this.accumulatedSquareSum / SamplesPerColumn;
                            double amplitude = Math.Log(meanSquare) / Math.Log(int.MaxValue);

                            // Renormalize signal above noise floor to [0,1] range.
                            this.energy[this.energyIndex] = Math.Max(0, amplitude - EnergyNoiseFloor) / (1 - EnergyNoiseFloor);
                            this.energyIndex = (this.energyIndex + 1) % this.energy.Length;

                            this.accumulatedSquareSum = 0;
                            this.accumulatedSampleCount = 0;
                            ++this.newEnergyAvailable;
                        }
                    }
                }
            }
        }


    }
}
