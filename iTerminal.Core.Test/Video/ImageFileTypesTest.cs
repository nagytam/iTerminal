using iTerminal.Core.Video;

namespace iTerminal.Core.Test.Video;

[TestClass]
public class ImageFileTypesTest
{
    [TestMethod]
    [DataRow("scan.psd")]
    [DataRow("big.PSB")]
    [DataRow("shot.cr2")]
    [DataRow("shot.CR3")]
    [DataRow("shot.nef")]
    [DataRow("shot.dng")]
    [DataRow("shot.arw")]
    [DataRow("picture.jp2")]
    [DataRow("picture.J2K")]
    [DataRow("picture.jpf")]
    public void TheNewFormats_OpenInThePictureViewer(string name)
    {
        Assert.IsTrue(ImageFileTypes.IsImageFile(name));
    }

    [TestMethod]
    [DataRow("notes.txt")]
    [DataRow("archive.zip")]
    [DataRow("clip.mp4")]
    public void OtherFiles_DoNotOpenInThePictureViewer(string name)
    {
        Assert.IsFalse(ImageFileTypes.IsImageFile(name));
    }

    [TestMethod]
    [DataRow("sheet.ppm")]
    [DataRow("mask.PGM")]
    [DataRow("mono.pbm")]
    [DataRow("alpha.pam")]
    [DataRow("scan.pcx")]
    [DataRow("old.sgi")]
    [DataRow("icon.xbm")]
    [DataRow("badge.xpm")]
    [DataRow("frame.tga")]
    [DataRow("render.exr")]
    [DataRow("sky.hdr")]
    [DataRow("film.dpx")]
    [DataRow("quick.qoi")]
    [DataRow("mobile.wbmp")]
    [DataRow("photo.jxl")]
    [DataRow("photo.avif")]
    public void FfmpegBitmaps_OpenInThePictureViewer(string name)
    {
        Assert.IsTrue(ImageFileTypes.IsImageFile(name));
    }

    [TestMethod]
    public void EachFamily_IsRecognisedByItsOwnTest()
    {
        Assert.IsTrue(ImageFileTypes.IsPhotoshopFile("a.psd"));
        Assert.IsFalse(ImageFileTypes.IsPhotoshopFile("a.cr2"));
        Assert.IsTrue(ImageFileTypes.IsRawFile("a.cr2"));
        Assert.IsFalse(ImageFileTypes.IsRawFile("a.jp2"));
        Assert.IsTrue(ImageFileTypes.Jpeg2000Extensions.Contains(".jp2"));
    }
}
