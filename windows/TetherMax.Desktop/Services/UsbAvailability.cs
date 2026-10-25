namespace TetherMax;
// A single transient ADB inventory/forward omission must not tear down a live
// tunnel. Two consecutive failed observations request cleanup once, never UAC.
internal sealed class UsbAvailability {
 int missing;
 public void Reset()=>missing=0;
 public bool Observe(bool phonePresent,bool channelsPresent){missing=phonePresent&&channelsPresent?0:Math.Min(3,missing+1);return missing==2;}
}
