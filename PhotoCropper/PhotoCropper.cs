using OpenCvSharp;

namespace PhotoCropper;

public class PhotoCropper
{
    private static readonly double MIN_AREA_THRESHOLD = 300; // Increase the threshold for larger areas
    private readonly string originalFilePath;

    public PhotoCropper(string originalFilePath)
    {
        this.originalFilePath = originalFilePath;
        Original = new Mat(originalFilePath, ImreadModes.Color);
        

        // Visualize the detected bounding boxes
        OriginalWithDetected = Original.Clone();
    }

    public Mat Original { get; set; }
    public Mat OriginalWithDetected { get; set; }
    public List<Mat> DetectedPhotos { get; set; } = new List<Mat>();

    public void DetectPhotos()
    {

        // Convert to grayscale
        var grayImage = new Mat();
        Cv2.CvtColor(Original, grayImage, ColorConversionCodes.BGR2GRAY);

        // Apply Gaussian Blur
        //Cv2.GaussianBlur(grayImage, grayImage, new OpenCvSharp.Size(5, 5), 0);

        // Apply adaptive thresholding
        var thresholdImage = new Mat();
        Cv2.AdaptiveThreshold(grayImage, thresholdImage, 50, AdaptiveThresholdTypes.GaussianC, ThresholdTypes.BinaryInv, 11, 2);

        // Apply edge detection
        var edges = new Mat();
        Cv2.Canny(thresholdImage, edges, 50, 150);

        // Find contours
        var contours = Cv2.FindContoursAsArray(edges, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

        // Combine small nearby contours
        foreach (var contour in contours)
        {
            var area = Cv2.ContourArea(contour);
            if (area > MIN_AREA_THRESHOLD)
            {
                // Approximate contour to polygon
                var approx = Cv2.ApproxPolyDP(contour, 0.02 * Cv2.ArcLength(contour, true), true);

                // Check if the polygon has 4 vertices (rectangle or square)
                if (approx.Length == 4)
                {
                    var boundingBox = Cv2.BoundingRect(approx);
                    double aspectRatio = (double)boundingBox.Width / boundingBox.Height;

                    Cv2.Rectangle(OriginalWithDetected, boundingBox, Scalar.Red, 2);

                    // Filter for approximate squares or rectangles
                    if (aspectRatio > 0.8 && aspectRatio < 1.25)
                    {
                        var croppedImage = new Mat(Original, boundingBox);

                        // Ensure the cropped image is large enough
                        if (croppedImage.Width > 500 && croppedImage.Height > 500)
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
            photo.SaveImage(Path.Combine(Path.GetDirectoryName(originalFilePath), Guid.NewGuid().ToString() + ".jpg"));
        }
    }
}
