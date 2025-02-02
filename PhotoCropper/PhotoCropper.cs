using Emgu.CV;
using Emgu.CV.Structure;
using Emgu.CV.CvEnum;
using System.Drawing;
using Emgu.CV.Util;

namespace PhotoCropper;

public class PhotoCropper
{

    private static double MIN_AREA_THRESHOLD = 0;
    private static double MAX_AREA_THRESHOLD = 0;
    private readonly string originalFilePath;

    public PhotoCropper(string originalFilePath)
    {
        this.originalFilePath = originalFilePath;
        Original = CvInvoke.Imread(originalFilePath, ImreadModes.Color);

        // Create a new Mat with larger dimensions by adding a border

        //TODO not sure id this border adding thing does somethnig useful

        //int borderSize = 20; // Adjust the border size as needed
        //Mat largerImage = new();
        //CvInvoke.CopyMakeBorder(Original, largerImage, borderSize, borderSize, borderSize, borderSize, BorderType.Constant, new MCvScalar(255, 255, 255)); // Set the border color to white

        //// Update the Original property to the new larger image
        //Original = largerImage;

        MIN_AREA_THRESHOLD = (Original.Width * Original.Height) * 0.10;
        MAX_AREA_THRESHOLD = (Original.Width * Original.Height) * 0.50;

        // Visualize the detected bounding boxes
        OriginalWithDetected = Original.Clone();
    }

    public Mat Original { get; set; }
    public Mat OriginalWithDetected { get; set; }
    public List<Mat> DetectedPhotos { get; set; } = [];

    public void DetectPhotos()
    {
        Mat grayImage = ConvertToGrayscaleAndBlur(Original);
        Mat binaryImage = ApplyThreshold(grayImage);
        DetectAndExtractPhotos(binaryImage, MAX_AREA_THRESHOLD, MIN_AREA_THRESHOLD);
    }

    private static Mat ConvertToGrayscaleAndBlur(Mat image)
    {
        var grayImage = new Mat();
        CvInvoke.CvtColor(image, grayImage, ColorConversion.Bgr2Gray);
        CvInvoke.GaussianBlur(grayImage, grayImage, new Size(5, 5), 1.5);
        return grayImage;
    }

    private static Mat ApplyThreshold(Mat grayImage)
    {
        Mat binaryImage = new();
        CvInvoke.Threshold(grayImage, binaryImage, 200, 255, ThresholdType.BinaryInv);
        return binaryImage;
    }

    private void DetectAndExtractPhotos(Mat binaryImage, double maximumAreaDetected, double minimalAreaDetected)
    {
        using VectorOfVectorOfPoint contours = new();
        CvInvoke.FindContours(binaryImage, contours, null, RetrType.Ccomp, ChainApproxMethod.ChainApproxSimple);

        for (int i = 0; i < contours.Size; i++)
        {
            double area = CvInvoke.ContourArea(contours[i]);
            if (area > minimalAreaDetected && area < maximumAreaDetected)
            {
                DetectedPhotos.Add(ExtractPhotoFromContour(contours[i]));
            }
        }
    }

    private Mat ExtractPhotoFromContour(VectorOfPoint contour)
    {
        RotatedRect minAreaRect = CvInvoke.MinAreaRect(contour);
        double angle = minAreaRect.Angle;
        if (Math.Abs(angle) > 45)
        {
            angle -= 90;
        }

        Mat rotationMatrix = new();
        CvInvoke.GetRotationMatrix2D(minAreaRect.Center, angle, 1.0, rotationMatrix);

        Mat rotatedImage = new();
        CvInvoke.WarpAffine(Original, rotatedImage, rotationMatrix, Original.Size, Inter.Linear, Warp.Default, BorderType.Constant, new MCvScalar(255, 255, 255));

        Rectangle boundingRect = minAreaRect.MinAreaRect();
        Mat detectedImage = new Mat(rotatedImage, boundingRect);

        // Convert the detected image to grayscale and apply a binary threshold
        Mat grayDetectedImage = new Mat();
        CvInvoke.CvtColor(detectedImage, grayDetectedImage, ColorConversion.Bgr2Gray);
        Mat binaryDetectedImage = new Mat();
        CvInvoke.Threshold(grayDetectedImage, binaryDetectedImage, 200, 255, ThresholdType.BinaryInv);

        // Find contours in the binary image
        using VectorOfVectorOfPoint detectedContours = new();
        CvInvoke.FindContours(binaryDetectedImage, detectedContours, null, RetrType.External, ChainApproxMethod.ChainApproxSimple);

        // Find the bounding box of the largest contour
        Rectangle cropRect = Rectangle.Empty;
        double maxArea = 0;
        for (int i = 0; i < detectedContours.Size; i++)
        {
            double area = CvInvoke.ContourArea(detectedContours[i]);
            if (area > maxArea)
            {
                maxArea = area;
                cropRect = CvInvoke.BoundingRectangle(detectedContours[i]);
            }
        }

        // Crop the detected image to the bounding box
        Mat croppedImage = new Mat(detectedImage, cropRect);

        // Draw the bounding box on the original image with detected bounding boxes
        CvInvoke.Rectangle(OriginalWithDetected, boundingRect, new MCvScalar(0, 255, 0), 2);

        return croppedImage;
    }

    public void SaveDetectedPhotos()
    {
        foreach (var photo in DetectedPhotos)
        {
            string fileName = Path.Combine(Path.GetDirectoryName(originalFilePath), Guid.NewGuid().ToString() + ".jpg");
            photo.Save(fileName);
        }
    }
}