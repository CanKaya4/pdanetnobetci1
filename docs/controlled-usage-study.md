# Kontrollü operatör sayaç deneyi

Amaç: telefon, standart RNDIS, PdaNet ve TetherMax deneylerinin operatörde görünen
değişimlerini ayrı kaydetmek. Uygulama sayaçları operatörün ücretlendirme
kayıtlarına erişmez. Sıfır görünen hotspot farkı, yuvarlama/gecikme nedeniyle
“kesin normal veriye yazıldı” anlamına gelmez.

Bu belge bir uygulama talimatı planıdır; fiziksel ağ deneyi için onay verilmeden
otomatik çalıştırılmaz. `usage-study.py` tamamen çevrimdışıdır; indirme yapmaz.

1. Deney başına en fazla 1 MiB uygulama payload'ı seçin. Araç üst sınırı 10 MiB
   kabul eder, fakat bunu otomatik harcamaz ve trafik sınırlandırıcısı değildir.
   Protokol ek yükleri ayrıca faturalandırılabilir. Büyük indirme sitesi kullanmayın.
2. Güncelleme, eşitleme, video ve diğer istemci trafiğini kullanıcı kendi
   kontrolünde durdursun. İki operatör sayacının birimini (MB/MiB ve yuvarlama),
   tarife dönemini ve kullanılan/saklı kalan miktar ayrımını kaydedin. Kalan kota
   gösteriliyorsa önce kullanılan bayta dönüştürün. Ekran görüntüsü referansı ekleyin.
3. Telefonun kendi tarayıcısında kontrollü küçük nesne kullanın; başlangıç/bitiş
   saatleri ve sayaçları kaydedin. Bu aşamada PC yolu üzerinden sonuç çıkarmayın.
4. Ayrı onaylı oturumda standart Android USB tethering/RNDIS ile aynı nesneyi
   kullanın; Windows seçili arayüzünü ve önce/sonra adaptör sayaçlarını kaydedin.
5. Önceki deneyin sayaçları yerleşmeden yeni deneye geçmeyin. 15/60 dakika ve
   gerekirse ertesi gün yeniden kontrol edin; bu süreler operatör garantisi değil,
   gözlem noktalarıdır. Değişmeye devam ederse sonuç `inconclusive` kalır.
   Ayrı C deneyinde PdaNet USB kullanın; aynı nesne, benzer radyo koşulları,
   uygulama/masaüstü sürümleri ve gerçek Hide Tether Usage ayarı kaydedilsin.
   A/B/C/D arasında aynı anda başka paylaşım istemcisi çalışmasın. C deneyinin
   ağ değişikliği ve veri bütçesi için ayrıca kullanıcı onayı gerekir.
6. Ayrı onaylı TetherMax oturumunda kullanıcı Android standart paylaşımının
   kapalı olduğunu gözlemlesin. Mevcut tek RNDIS bağlantısını bu hazırlık sırasında
   kapatmayın. `ip-check` yalnızca in-memory IP yolunu sınar; normal Windows
   uygulamalarının Wintun yolunda olduğunu onaylamaz. İki kanıtı karıştırmayın.
7. TetherMax Windows payload ve Android relay payload sayaçlarını ayrı saklayın.
   `ip-check` artık Android önce/sonra değerlerini ve farklarını raporlar.
   Android sayaçları tüm relay oturumlarına aittir; paralel relay trafiği olmasın.
   Android ağı veya sayaçlar değişirse deney geçersizdir.
8. `usage-study.py record.json` ile offline fark raporu üretin. Operatör
   sınıflandırmasını hiçbir zaman otomatik doğrulanmış saymaz. Diğer üç aşama
   için ayrı JSON dosyaları kullanın; aynı zaman aralığında karıştırmayın.

JSON alanları: `mode` = `phone` / `rndis` / `pdanet` / `tethermax`, `budgetBytes`, zaman
dilimli ISO8601 `startedAt`/`endedAt`, `backgroundTrafficQuiet`,
`carrierCountersSettled`, `carrierBeforeEvidence`, `carrierAfterEvidence`.
`before` ve `after` içinde bayt cinsinden `windowsUpload`, `windowsDownload`,
`androidUpload`, `androidDownload`, `carrierMobileUsed`, `carrierHotspotUsed`
zorunludur. Telefon deneyinde PC sayaçlarını sıfır tutun; telefonun sistem
sayacını relay sayacı gibi sunmayın. TetherMax için ayrıca
`standardTetheringOffObserved=true`, `pathEvidence="native-wintun"` gerekir.
Bu kullanıcı girdileri bağımsız kriptografik doğrulama değildir.

Yeni şablon için `usage-study.py --template phone` (veya `rndis`, `pdanet`,
`tethermax`) kullanın. Şablon izin verilmiş bir deney değildir: onay alanı false,
kanıtlar boştur. Araç ayrıca bilinen nesne boyutu `sampleBytes`,
`dataTestApproved`, iki sayacın bayt çözünürlüğü `carrierResolutionBytes`, seçili
adaptör/kanıt dosyası `selectedPath` ve zamanlı `carrierReadings` dizisini ister.
PdaNet için `pdanetVersion` ve gerçek `hideTetherUsageEnabled` gerekir.

Örneğin bilinen nesne 256 KiB, sayaç çözünürlüğü 1 MB ise küçük deney kota
davranışını ayırt edemez; araç bunu inconclusive sayar. Ölçeği büyütmek yeni
veri bütçesi ve ayrı onay gerektirir. MB ile MiB farkını açıkça dönüştürün.
Baseline ve en az iki gecikmeli operatör okuması girilir; son iki okuma aynı
değilse sonuç belirsiz kalır. `observedCarrierUpdateDelaySeconds` son değişimin
gözlendiği andır; gizli operatörün gerçek işleme gecikmesi ölçülmüş sayılmaz.

Native Windows aşamasında 120 saniye/1 MiB payload sınırı yazılımda vardır;
offline analiz aracı kendi başına tüketim sınırlamaz. Başlangıç/bitiş zamanı,
Windows/Android ayrı sayaçları ve seçili yol [native raporlarda](native-network.md)
tutulur. Operatör kayıtları uygulama tarafından otomatik okunmaz.
Tarife uygulaması bir sayacı/çözünürlüğü göstermiyorsa JSON'da `null` girin;
görünmeyen değer sıfır değildir. Araç bu kaydı belirsiz tutar.
