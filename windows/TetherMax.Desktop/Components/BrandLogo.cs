using System.IO;
using System.Reflection;
using System.Windows.Media.Imaging;
namespace TetherMax;
internal static class BrandLogo {
 public static BitmapImage Load(){using var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("TetherMax.Brand")??throw new IOException("TetherMax marka kaynağı bulunamadı.");var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.StreamSource=stream;image.EndInit();image.Freeze();return image;}
}
