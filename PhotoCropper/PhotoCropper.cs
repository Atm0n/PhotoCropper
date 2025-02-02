using Emgu.CV;
using Emgu.CV.Structure;
using Emgu.CV.CvEnum;
using System.Drawing;
using Emgu.CV.Util;

namespace PhotoCropper;

public class PhotoCropper
{
    private static double MIN_AREA_THRESHOLD = 300; // Increase the threshold for larger areas
    private readonly string originalFilePath;

    public PhotoCropper(string originalFilePath)
    {
        this.originalFilePath = originalFilePath;
        Original = CvInvoke.Imread(originalFilePath, ImreadModes.Color);

        // Create a new Mat with larger dimensions by adding a border
        int borderSize = 20; // Adjust the border size as needed
        Mat largerImage = new Mat();
        CvInvoke.CopyMakeBorder(Original, largerImage, borderSize, borderSize, borderSize, borderSize, BorderType.Constant, new MCvScalar(255, 255, 255)); // Set the border color to white

        // Update the Original property to the new larger image
        //Original = largerImage;

        MIN_AREA_THRESHOLD = (Original.Width * Original.Height) / 11;

        // Visualize the detected bounding boxes
        OriginalWithDetected = Original.Clone();
    }

    public Mat Original { get; set; }
    public Mat OriginalWithDetected { get; set; }
    public List<Mat> DetectedPhotos { get; set; } = new List<Mat>();



    public void DetectPhotos()
    {
        // Convert the image to grayscale
        Mat grayImage = new Mat();
        CvInvoke.CvtColor(Original, grayImage, ColorConversion.Bgr2Gray);

        // Apply a GaussianBlur to reduce noise and improve contour detection
        CvInvoke.GaussianBlur(grayImage, grayImage, new Size(5, 5), 1.5);

        // Apply a threshold to binarize the image
        Mat binaryImage = new Mat();
        CvInvoke.Threshold(grayImage, binaryImage, 200, 255, ThresholdType.BinaryInv);

        // Find contours in the binary image
        using (VectorOfVectorOfPoint contours = new VectorOfVectorOfPoint())
        {
            CvInvoke.FindContours(binaryImage, contours, null, RetrType.External, ChainApproxMethod.ChainApproxSimple);

            // Filter contours based on area to detect smaller images
            for (int i = 0; i < contours.Size; i++)
            {
                double area = CvInvoke.ContourArea(contours[i]);
                if (area > MIN_AREA_THRESHOLD)
                {
                    // Get the bounding rectangle of the contour
                    Rectangle boundingRect = CvInvoke.BoundingRectangle(contours[i]);

                    // Extract the detected image
                    Mat detectedImage = new Mat(Original, boundingRect);
                    DetectedPhotos.Add(detectedImage);

                    // Draw the bounding rectangle on the OriginalWithDetected image
                    CvInvoke.Rectangle(OriginalWithDetected, boundingRect, new MCvScalar(0, 255, 0), 2);
                }
            }
        }
        grayImage.Save(Path.Combine(Path.GetDirectoryName(originalFilePath), "grayImage.jpg"));
        binaryImage.Save(Path.Combine(Path.GetDirectoryName(originalFilePath), "binaryImage.jpg"));
        OriginalWithDetected.Save(Path.Combine(Path.GetDirectoryName(originalFilePath), "OriginalWithDetected.jpg"));
    }


    public void SaveDetectedPhotos()
    {
        // Save the detected photos to the output directory
        foreach (var photo in DetectedPhotos)
        {
            string fileName = Path.Combine(Path.GetDirectoryName(originalFilePath), Guid.NewGuid().ToString() + ".jpg");
            photo.Save(fileName);
        }
    }
}