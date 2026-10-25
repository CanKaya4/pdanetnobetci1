package io.tethermax.app;
import java.util.*;
/** TCP handshake samples; neither ICMP ping nor throughput nor radio signal. */
public final class PositionQuality {
 private PositionQuality(){}
 public static String summarize(double[][] samples){
  List<Double> valid=new ArrayList<>();int total=0,failed=0,pairs=0;double jitter=0;
  for(double[] endpoint:samples){double previous=-1;for(double sample:endpoint){total++;if(!Double.isFinite(sample)||sample<0){failed++;previous=-1;continue;}valid.add(sample);if(previous>=0){jitter+=Math.abs(sample-previous);pairs++;}previous=sample;}}
  if(total<20)return "Yetersiz örnek; testi tamamlayın.";
  if(valid.size()<10)return "Bu konumda bağlantı güvenilir değil veya test hedefleri erişilemiyor. Başka bir konumda tekrar ölçün.";
  Collections.sort(valid);double median=valid.get((valid.size()-1)/2),p95=valid.get((int)Math.ceil(valid.size()*.95)-1),variation=pairs==0?Double.NaN:jitter/pairs,loss=failed*100.0/total;
  String grade=failed==0&&p95<=120&&variation<=25?"Bu ölçümde konum uygun":loss<=10&&p95<=200&&variation<=60?"Konum kullanılabilir; oyun için başka konumla karşılaştırın":"Dalgalanma yüksek; pencere yakını veya farklı yükseklik deneyin";
  return String.format(Locale.ROOT,"%s\nTCP medyan %.0f ms · P95 %.0f ms\nArdışık örnek değişimi %.0f ms · Başarısız %d/%d (%%%.0f)\nBu sonuç kısa süreli TCP ölçümüdür; oyun pingini, Mbps hızını veya operatör kotasını ölçmez.",grade,median,p95,variation,failed,total,loss);
 }
}
