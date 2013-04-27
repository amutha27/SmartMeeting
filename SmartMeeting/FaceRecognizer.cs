using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Emgu.CV.UI;
using Emgu.CV;
using Emgu.CV.Structure;
using Emgu.CV.CvEnum;

using System.IO;
using System.Xml;
using System.Runtime.InteropServices;
using System.Threading;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
using System.Drawing;
using System.Windows;
namespace USC.Robotics.SmartMeeting
{
    class FaceRecognizer
    {
        #region variables
        Image<Bgr, Byte> currentFrame;
        Image<Gray, byte> gray_frame = null;

        public HaarCascade Face = new HaarCascade(AppDomain.CurrentDomain.BaseDirectory + "/Cascades/haarcascade_frontalface_alt2.xml");


        //Declararation of all variables, vectors and haarcascades
        HaarCascade face;
        HaarCascade eye;
        MCvFont font = new MCvFont(FONT.CV_FONT_HERSHEY_TRIPLEX, 0.5d, 0.5d);
        Image<Gray, byte> result, TrainedFace = null;
        Image<Gray, byte> gray = null;
        static List<Image<Gray, byte>> trainingImages = new List<Image<Gray, byte>>();
        static List<string> labels = new List<string>();
        List<string> NamePersons = new List<string>();
        static int ContTrain, NumLabels, t;
        string name, names = null;
        #endregion
        static FaceRecognizer(){
            try
            {
                //Load of previus trainned faces and labels for each image
                string Labelsinfo = File.ReadAllText(AppDomain.CurrentDomain.BaseDirectory + "/TrainedFaces/TrainedLabels.txt");
                string[] Labels = Labelsinfo.Split('%');
                NumLabels = Convert.ToInt16(Labels[0]);
                ContTrain = NumLabels;
                string LoadFaces;

                for (int tf = 1; tf < NumLabels + 1; tf++)
                {
                    LoadFaces = "face" + tf + ".bmp";
                    trainingImages.Add(new Image<Gray, byte>(AppDomain.CurrentDomain.BaseDirectory + "/TrainedFaces/" + LoadFaces));
                    labels.Add(Labels[tf]);
                }

            }
            catch (Exception ex)
            { }
        }
        internal string getFaceTag(Bitmap sourceBmp)
        {
            //Get the current frame form capture device
            currentFrame = new Image<Bgr, byte>(sourceBmp).Resize(320, 240, Emgu.CV.CvEnum.INTER.CV_INTER_LINEAR);

            if (currentFrame != null)
            {
                gray_frame = currentFrame.Convert<Gray, Byte>();

                //Face Detector
                MCvAvgComp[][] facesDetected = gray_frame.DetectHaarCascade(
                    Face,
                    1.2,
                    1,
                    Emgu.CV.CvEnum.HAAR_DETECTION_TYPE.DO_CANNY_PRUNING,
                    new System.Drawing.Size(20, 20));

                foreach (MCvAvgComp f in facesDetected[0])
                {
                    t = t + 1;
                    result = currentFrame.Copy(f.rect).Convert<Gray, byte>().Resize(100, 100, Emgu.CV.CvEnum.INTER.CV_INTER_CUBIC);
                    //draw the face detected in the 0th (gray) channel with blue color
                    //currentFrame.Draw(f.rect, new Bgr(Color.Red), 2);


                    if (trainingImages.ToArray().Length != 0)
                    {
                        //TermCriteria for face recognition with numbers of trained images like maxIteration
                        MCvTermCriteria termCrit = new MCvTermCriteria(ContTrain, 0.001);

                        //Eigen face recognizer
                        EigenObjectRecognizer recognizer = new EigenObjectRecognizer(
                           trainingImages.ToArray(),
                           labels.ToArray(),
                           3000,
                           ref termCrit);

                        name = recognizer.Recognize(result) ;
                        if (!name.Equals("")&&name!=null)
                        {
                            return name;
                        }
                    }
                }
            }
            return "Unknown" ;
        }
    }
}
