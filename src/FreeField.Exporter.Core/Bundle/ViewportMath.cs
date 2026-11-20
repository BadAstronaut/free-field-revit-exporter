using FreeField.Exporter.Core.Geometry;
using FreeField.Exporter.Core.Model;

namespace FreeField.Exporter.Core;

public static class ViewportMath
{
    /// <summary>
    /// Two control points at opposite corners of <paramref name="pdfRegion"/> (top-left and bottom-right in PDF space),
    /// with model coordinates computed exactly through <paramref name="pdfToModel"/>.
    /// </summary>
    public static List<ControlPoint> ControlPointsFor(Affine2D pdfToModel, BBox2 pdfRegion)
    {
        ControlPoint Make(double px, double py)
        {
            var m = pdfToModel.Apply(new Point2(px, py));
            return new ControlPoint { PdfX = px, PdfY = py, ModelX = m.X, ModelY = m.Y };
        }
        return new List<ControlPoint> { Make(pdfRegion.MinX, pdfRegion.MinY), Make(pdfRegion.MaxX, pdfRegion.MaxY) };
    }
}
