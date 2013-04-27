using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using Microsoft.Kinect;
using Microsoft.Kinect.Toolkit.FaceTracking;

using Point = System.Windows.Point;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace USC.Robotics.SmartMeeting
{
    /// <summary>
    /// Class that uses the Face Tracking SDK to display a face mask for
    /// tracked skeletons
    /// </summary>
    public partial class FaceTagViewer : UserControl, IDisposable
    {
        public static readonly DependencyProperty KinectProperty = DependencyProperty.Register(
            "Kinect",
            typeof(KinectSensor),
            typeof(FaceTagViewer),
            new PropertyMetadata(
                null, (o, args) => ((FaceTagViewer)o).OnSensorChanged((KinectSensor)args.OldValue, (KinectSensor)args.NewValue)));

        private const uint MaxMissedFrames = 50;

        private readonly Dictionary<int, SkeletonFaceTracker> trackedSkeletons = new Dictionary<int, SkeletonFaceTracker>();

        private byte[] colorImage;

        private ColorImageFormat colorImageFormat = ColorImageFormat.Undefined;

        private short[] depthImage;

        private DepthImageFormat depthImageFormat = DepthImageFormat.Undefined;

        private bool disposed;

        private Skeleton[] skeletonData;

        public FaceTagViewer()
        {
            this.InitializeComponent();
        }

        ~FaceTagViewer()
        {
            this.Dispose(false);
        }

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

        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!this.disposed)
            {
                this.ResetFaceTracking();

                this.disposed = true;
            }
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            //This transform is applied as the target image is scaled
            drawingContext.PushTransform(new ScaleTransform(0.25, 0.25));

            base.OnRender(drawingContext);
            foreach (SkeletonFaceTracker faceInformation in this.trackedSkeletons.Values)
            {
                //faceInformation.DrawFaceModel(drawingContext);
                faceInformation.DrawFaceRect(drawingContext);
                faceInformation.DrawFaceTag(drawingContext);
            }
        }

        private void OnAllFramesReady(object sender, AllFramesReadyEventArgs allFramesReadyEventArgs)
        {
            ColorImageFrame colorImageFrame = null;
            DepthImageFrame depthImageFrame = null;
            SkeletonFrame skeletonFrame = null;

            try
            {
                colorImageFrame = allFramesReadyEventArgs.OpenColorImageFrame();
                depthImageFrame = allFramesReadyEventArgs.OpenDepthImageFrame();
                skeletonFrame = allFramesReadyEventArgs.OpenSkeletonFrame();

                if (colorImageFrame == null || depthImageFrame == null || skeletonFrame == null)
                {
                    return;
                }

                // Check for image format changes.  The FaceTracker doesn't
                // deal with that so we need to reset.
                if (this.depthImageFormat != depthImageFrame.Format)
                {
                    this.ResetFaceTracking();
                    this.depthImage = null;
                    this.depthImageFormat = depthImageFrame.Format;
                }

                if (this.colorImageFormat != colorImageFrame.Format)
                {
                    this.ResetFaceTracking();
                    this.colorImage = null;
                    this.colorImageFormat = colorImageFrame.Format;
                }

                // Create any buffers to store copies of the data we work with
                if (this.depthImage == null)
                {
                    this.depthImage = new short[depthImageFrame.PixelDataLength];
                }

                if (this.colorImage == null)
                {
                    this.colorImage = new byte[colorImageFrame.PixelDataLength];
                }

                // Get the skeleton information
                if (this.skeletonData == null || this.skeletonData.Length != skeletonFrame.SkeletonArrayLength)
                {
                    this.skeletonData = new Skeleton[skeletonFrame.SkeletonArrayLength];
                }

                colorImageFrame.CopyPixelDataTo(this.colorImage);
                depthImageFrame.CopyPixelDataTo(this.depthImage);
                skeletonFrame.CopySkeletonDataTo(this.skeletonData);

                // Update the list of trackers and the trackers with the current frame information
                foreach (Skeleton skeleton in this.skeletonData)
                {
                    if (skeleton.TrackingState == SkeletonTrackingState.Tracked)
                    {
                        // We want keep a record of any skeleton, tracked or untracked.
                        if (!this.trackedSkeletons.ContainsKey(skeleton.TrackingId))
                        {
                            this.trackedSkeletons.Add(skeleton.TrackingId, new SkeletonFaceTracker(colorImageFrame));
                        }

                        // Give each tracker the upated frame.
                        SkeletonFaceTracker skeletonFaceTracker;
                        if (this.trackedSkeletons.TryGetValue(skeleton.TrackingId, out skeletonFaceTracker))
                        {
                            skeletonFaceTracker.OnFrameReady(this.Kinect, colorImageFormat, colorImage, depthImageFormat, depthImage, skeleton);
                            skeletonFaceTracker.LastTrackedFrame = skeletonFrame.FrameNumber;
                        }
                    }
                }

                this.RemoveOldTrackers(skeletonFrame.FrameNumber);

                this.InvalidateVisual();
            }
            finally
            {
                if (colorImageFrame != null)
                {
                    colorImageFrame.Dispose();
                }

                if (depthImageFrame != null)
                {
                    depthImageFrame.Dispose();
                }

                if (skeletonFrame != null)
                {
                    skeletonFrame.Dispose();
                }
            }
        }

        private void OnSensorChanged(KinectSensor oldSensor, KinectSensor newSensor)
        {
            if (oldSensor != null)
            {
                oldSensor.AllFramesReady -= this.OnAllFramesReady;
                this.ResetFaceTracking();
            }

            if (newSensor != null)
            {
                newSensor.AllFramesReady += this.OnAllFramesReady;
            }
        }

        /// <summary>
        /// Clear out any trackers for skeletons we haven't heard from for a while
        /// </summary>
        private void RemoveOldTrackers(int currentFrameNumber)
        {
            var trackersToRemove = new List<int>();
            foreach (var tracker in this.trackedSkeletons)
            {
                uint missedFrames = (uint)currentFrameNumber - (uint)tracker.Value.LastTrackedFrame;
                if (missedFrames > MaxMissedFrames)
                {
                    // There have been too many frames since we last saw this skeleton
                    trackersToRemove.Add(tracker.Key);
                }
            }

            foreach (int trackingId in trackersToRemove)
            {
                this.RemoveTracker(trackingId);
            }
        }

        private void RemoveTracker(int trackingId)
        {
            this.trackedSkeletons[trackingId].Dispose();
            this.trackedSkeletons.Remove(trackingId);
        }

        private void ResetFaceTracking()
        {
            foreach (int trackingId in new List<int>(this.trackedSkeletons.Keys))
            {
                this.RemoveTracker(trackingId);
            }
        }

        private class SkeletonFaceTracker : IDisposable
        {
            private static FaceTriangle[] faceTriangles;

            private string faceTag;

            private EnumIndexableCollection<FeaturePoint, Microsoft.Kinect.Toolkit.FaceTracking.PointF> facePoints;

            private FaceTracker faceTracker;

            private bool lastFaceTrackSucceeded;

            private SkeletonTrackingState skeletonTrackingState;
            private Microsoft.Kinect.Toolkit.FaceTracking.Rect faceRect;
            private System.Drawing.Bitmap colorImageBmp;

            public SkeletonFaceTracker(ColorImageFrame colorImageFrame)
            {
                byte[] pixeldata = new byte[colorImageFrame.PixelDataLength];
                colorImageFrame.CopyPixelDataTo(pixeldata);
                colorImageBmp = new System.Drawing.Bitmap(colorImageFrame.Width, colorImageFrame.Height, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
                System.Drawing.Imaging.BitmapData bmapdata = colorImageBmp.LockBits(
                    new System.Drawing.Rectangle(0, 0, colorImageFrame.Width, colorImageFrame.Height),
                    System.Drawing.Imaging.ImageLockMode.WriteOnly,
                    colorImageBmp.PixelFormat);
                IntPtr ptr = bmapdata.Scan0;
                Marshal.Copy(pixeldata, 0, ptr, colorImageFrame.PixelDataLength);
                colorImageBmp.UnlockBits(bmapdata);
            }

            public int LastTrackedFrame { get; set; }

            public void Dispose()
            {
                if (this.faceTracker != null)
                {
                    this.faceTracker.Dispose();
                    this.faceTracker = null;
                }
            }

            public void DrawFaceModel(DrawingContext drawingContext)
            {
                if (!this.lastFaceTrackSucceeded || this.skeletonTrackingState != SkeletonTrackingState.Tracked)
                {
                    return;
                }

                var faceModelPts = new List<Point>();
                var faceModel = new List<FaceModelTriangle>();

                for (int i = 0; i < this.facePoints.Count; i++)
                {
                    faceModelPts.Add(new Point(this.facePoints[i].X + 0.5f, this.facePoints[i].Y + 0.5f));
                }

                foreach (var t in faceTriangles)
                {
                    var triangle = new FaceModelTriangle();
                    triangle.P1 = faceModelPts[t.First];
                    triangle.P2 = faceModelPts[t.Second];
                    triangle.P3 = faceModelPts[t.Third];
                    faceModel.Add(triangle);
                }

                var faceModelGroup = new GeometryGroup();
                for (int i = 0; i < faceModel.Count; i++)
                {
                    var faceTriangle = new GeometryGroup();
                    faceTriangle.Children.Add(new LineGeometry(faceModel[i].P1, faceModel[i].P2));
                    faceTriangle.Children.Add(new LineGeometry(faceModel[i].P2, faceModel[i].P3));
                    faceTriangle.Children.Add(new LineGeometry(faceModel[i].P3, faceModel[i].P1));
                    faceModelGroup.Children.Add(faceTriangle);
                }

                drawingContext.DrawGeometry(Brushes.LightYellow, new Pen(Brushes.LightYellow, 1.0), faceModelGroup);
            }

            /// <summary>
            /// Updates the face tracking information for this skeleton
            /// </summary>
            internal void OnFrameReady(KinectSensor kinectSensor, ColorImageFormat colorImageFormat, byte[] colorImage, DepthImageFormat depthImageFormat, short[] depthImage, Skeleton skeletonOfInterest)
            {
                this.skeletonTrackingState = skeletonOfInterest.TrackingState;

                if (this.skeletonTrackingState != SkeletonTrackingState.Tracked)
                {
                    // if the current skeleton is not tracked, track it now
                    //kinectSensor.SkeletonStream.ChooseSkeletons(skeletonOfInterest.TrackingId);
                }

                if (this.faceTracker == null)
                {
                    try
                    {
                        this.faceTracker = new FaceTracker(kinectSensor);
                    }
                    catch (InvalidOperationException)
                    {
                        // During some shutdown scenarios the FaceTracker
                        // is unable to be instantiated.  Catch that exception
                        // and don't track a face.
                        Debug.WriteLine("AllFramesReady - creating a new FaceTracker threw an InvalidOperationException");
                        this.faceTracker = null;
                    }
                }

                if (this.faceTracker != null)
                {
                    // hack to make this face tracking detect the face even when it is not actually tracked
                    // <!>need to confirm if it works
                    //skeletonOfInterest.TrackingState = SkeletonTrackingState.Tracked;

                    FaceTrackFrame frame = this.faceTracker.Track(
                        colorImageFormat, colorImage, depthImageFormat, depthImage, skeletonOfInterest);
                    //new Microsoft.Kinect.Toolkit.FaceTracking.Rect(skeletonOfInterest.Position.));

                    this.lastFaceTrackSucceeded = frame.TrackSuccessful;
                    if (this.lastFaceTrackSucceeded)
                    {
                        if (faceTriangles == null)
                        {
                            // only need to get this once.  It doesn't change.
                            faceTriangles = frame.GetTriangles();

                        }
                        if (faceTag == null)
                        {
                            // here call the face detection
                            faceTag = new FaceRecognizer().getFaceTag(this.colorImageBmp);
                            
                            if (faceTag != null)
                            {
                                if (Global.trackedPeople.ContainsKey(skeletonOfInterest))
                                    Global.trackedPeople[skeletonOfInterest] = faceTag;
                                else
                                    Global.trackedPeople.Add(skeletonOfInterest, faceTag);
                            }
                        }
                        this.facePoints = frame.GetProjected3DShape();
                        this.faceRect = frame.FaceRect;
                    }
                }
            }

            private struct FaceModelTriangle
            {
                public Point P1;
                public Point P2;
                public Point P3;
            }

            internal void DrawFaceRect(DrawingContext drawingContext)
            {
                if (!this.lastFaceTrackSucceeded || this.skeletonTrackingState != SkeletonTrackingState.Tracked)
                {
                    return;
                }
                if (this.faceRect != null)
                {
                    var rect = new System.Windows.Rect(new Point(faceRect.Left, faceRect.Top), new Point(faceRect.Right, faceRect.Bottom));
                    drawingContext.DrawRectangle(null, new Pen(Brushes.Red, 1.0), rect);
                }
            }

            internal void DrawFaceTag(DrawingContext drawingContext)
            {
                if (!this.lastFaceTrackSucceeded || (this.skeletonTrackingState != SkeletonTrackingState.Tracked
                    && this.skeletonTrackingState != SkeletonTrackingState.PositionOnly))
                {
                    return;
                }
                if (this.faceTag != null)
                {
                    drawingContext.DrawText(
                        new FormattedText(faceTag, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Verdana"), 70, Brushes.DarkGreen),
                        new Point(faceRect.Left - 50+faceTag.Length, faceRect.Top - 200));
                }
            }

        }
    }
}
