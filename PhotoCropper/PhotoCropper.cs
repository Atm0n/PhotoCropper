using Emgu.CV;
using Emgu.CV.Structure;
using Emgu.CV.CvEnum;
using System.Drawing;
using Emgu.CV.Util;

namespace PhotoCropper;

public class PhotoCropper
{
    private static readonly double MIN_AREA_THRESHOLD = 150; // Increase the threshold for larger areas
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
        Original = largerImage;

        // Visualize the detected bounding boxes
        OriginalWithDetected = Original.Clone();
    }

    public Mat Original { get; set; }
    public Mat OriginalWithDetected { get; set; }
    public List<Mat> DetectedPhotos { get; set; } = new List<Mat>();

    public void DetectPhotos()
    {
        // Convert to grayscale
        Mat gray = new Mat();
        CvInvoke.CvtColor(Original, gray, ColorConversion.Bgr2Gray);
        OriginalWithDetected = gray;

        // Apply Gaussian blur
        Mat blurred = new Mat();
        CvInvoke.GaussianBlur(gray, blurred, new Size(5, 5), 0);

        // Use adaptive thresholding
        Mat thresh = new Mat();
        CvInvoke.AdaptiveThreshold(blurred, thresh, 255, AdaptiveThresholdType.GaussianC, ThresholdType.BinaryInv, 11, 2);

        // Find contours
        Mat hierarchy = new Mat();
        VectorOfVectorOfPoint contours = new VectorOfVectorOfPoint();
        CvInvoke.FindContours(thresh, contours, hierarchy, RetrType.External, ChainApproxMethod.ChainApproxSimple);

        // Iterate through contours and save each detected photo
        for (int i = 0; i < contours.Size; i++)
        {
            double area = CvInvoke.ContourArea(contours[i]);
            if (area > MIN_AREA_THRESHOLD)
            {
                // Approximate contour to polygon
                VectorOfPoint approx = new VectorOfPoint();
                CvInvoke.ApproxPolyDP(contours[i], approx, 0.02 * CvInvoke.ArcLength(contours[i], true), true);

                // Check if the polygon has 4 vertices (rectangle or square)
                if (approx.Size == 4)
                {
                    Rectangle boundingBox = CvInvoke.BoundingRectangle(approx);
                    double aspectRatio = (double)boundingBox.Width / boundingBox.Height;

                    CvInvoke.Rectangle(OriginalWithDetected, boundingBox, new MCvScalar(0, 0, 255), 2);

                    // Filter for approximate squares or rectangles
                    if (aspectRatio > 0 && aspectRatio < 500)
                    {
                        Mat croppedImage = new Mat(Original, boundingBox);

                        // Ensure the cropped image is large enough
                        if (croppedImage.Width > 200 && croppedImage.Height > 200)
                        {
                            DetectedPhotos.Add(croppedImage);
                        }
                    }
                }
            }
        }
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