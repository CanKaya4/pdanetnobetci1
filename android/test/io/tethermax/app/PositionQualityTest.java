package io.tethermax.app;
public final class PositionQualityTest {
 static void require(boolean value){if(!value)throw new AssertionError();}
 public static void main(String[] args){
  double[][] good=new double[2][10];java.util.Arrays.fill(good[0],30);java.util.Arrays.fill(good[1],70);
  String result=PositionQuality.summarize(good);require(result.contains("konum uygun")&&result.contains("değişimi 0"));
  good[0][0]=-1;require(!PositionQuality.summarize(good).contains("konum uygun"));
  for(double[] endpoint:good)java.util.Arrays.fill(endpoint,-1);require(PositionQuality.summarize(good).contains("güvenilir değil"));
  for(double[] endpoint:good)for(int i=0;i<10;i++)endpoint[i]=i%2==0?40:500;require(PositionQuality.summarize(good).contains("Dalgalanma yüksek"));
  require(PositionQuality.summarize(new double[1][2]).contains("Yetersiz"));
  good[0][0]=Double.NaN;require(!PositionQuality.summarize(good).contains("konum uygun"));
  System.out.println("PASS position: endpoint baselines separated, failure, high jitter, missing and non-finite samples");
 }
}
