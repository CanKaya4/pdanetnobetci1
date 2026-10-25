# TetherMax 0.7.0

USB internet motoru korunarak bağlantı yönetimi ve Windows/Android arayüzleri yenilendi.

## Bağlantı düzeltmeleri

- Bağlantı, iptal, temizlik ve profil işlemleri aynı oturum kilidiyle sıralanır. İkinci bağlantı isteği yeni oturum oluşturmaz. Devam eden cihaz yenilemesi beklenirken temizlik kilidi tutulmaz.
- Sabit 250 ms bekleme kaldırıldı. Android servis kaydı, güvenli eşleştirme, yerel motor dinleyicisi, worker bildirimi ve Windows sağlık sonucu kullanılır.
- Gerçek cihazda bulunan hata düzeltildi: Android ekranı zaten açıkken gelen bağlantı isteği yalnızca `onCreate` içinde işleniyordu. `onNewIntent` de isteği işler; çalışan servis yeniden başlatılmaz. İstek tüketildikten sonra kaldırılır.
- Telefon arama/servis/dinleyici beklemeleri sınırlıdır. Eşleştirmede yalnızca geçici soket/erken kapanma hataları yeniden denenir; kimlik ve kriptografi hataları yeniden denenmez.
- Otomatik bağlantı denemeleri exponential backoff ve jitter kullanır; beş başarısız denemeden sonra kullanıcı eylemi beklenir. Yetkisiz ADB ve reddedilen UAC sonsuz denemeye girmez.
- Guardian kapanması ve üç ardışık hücresel kontrol kaybı kurtarmaya yönlendirilir. Tek geçici örnek bağlantıyı düşürmez. Bunun için ek DNS/HTTPS sorgusu üretilmez.
- Eşzamanlı `DisposeAsync` çağrıları aynı kurtarma tamamlanmasını bekler. Guardian çıkışı doğrulanmadan yeni oturum başlatılmaz; kurtarma zaman aşımı gizlenmez.
- Bağlı durumu yalnızca beklenen üç farklı HTTPS/sistem DNS/UDP-DNS kontrolünün tamamı başarılıysa gösterilir. Boş veya eksik sonuç başarı sayılmaz.
- Yerel `reports/connection-stages.jsonl` kaydı deneme kimliği, aşama ve süreleri tutar; eşleştirme anahtarı veya relay içeriği yazılmaz. Dosya boyutu sınırlıdır.

## Arayüz ve marka

Windows uygulaması XAML View, ViewModel, Services, Models, Components ve merkezi tema dosyalarına ayrıldı. Yedi sayfa, büyük bağlantı kontrolü, gerçek telemetri kartları, bağlantı süresi, açık/koyu tema ve 1/5/30 dakika grafik aralığı eklendi. Gelişmiş araçlar Tanılama sayfasındadır.

Android Java altyapısı korunur. Material 3 tasarım ilkelerinden yararlanan yuvarlak kartlar, tonal düğmeler, beş bölümlü alt gezinme, profil kartları, açık/koyu tema ve sistem çubuğu boşlukları kullanılır. AndroidX Material 3 kitaplığına veya Compose'a geçilmedi; mevcut framework tabanlı derleme ve servis kimliği korundu.

Özgün SVG, çok boyutlu ICO/PNG, adaptive foreground, raster launcher boyutları, splash ve şeffaf tek renk bildirim ikonu eklendi. Windows EXE, MSI, Başlat/masaüstü kısayolu ve durum tepsisi ikonları paketlenir. Marka PNG'si açık bir manifest resource adıyla gömülür; hatalı pack URI çözümüne bağımlı değildir.

Android siyah ekran görüntüsünün nedeni bütün Activity'ye uygulanan `FLAG_SECURE` idi. Normal ekranlar görüntülenebilir; güvenlik koruması eşleştirme kodu penceresinde kalır.

## Canlı profiller

`profile-set` mevcut kimliği doğrulanmış relay kontrol kanalını kullanır. Windows seçimi telefon onayı ve mevcut worker telemetrisindeki istenen profil görülmeden başarılı sayılmaz. Wintun, rota veya worker yeniden başlatılmaz. Paket/heartbeat protokolü değiştirilmedi. Hız tavanı ve adaptif ayar değişiklikleri sonraki bağlantıda uygulanır; profil değişimi canlıdır. Ölçülmemiş oyun/hız kazancı vaat edilmez.

## Ölçülen örnekler — 24 Ekim 2026

Galaxy A56 / Android 16 ve Windows 11 üzerinde:

| Örnek | Seçili Wintun rotası | Sağlık kontrollerinin bitişi |
| --- | ---: | ---: |
| 0.6.0 kayıtlı cihaz | 16,14 sn | 16,56 sn |
| 0.7 geliştirme paketi, Android servisi kapalı | 10,96 sn | 11,46 sn |
| Son kurulu 0.7 / f197610, çift tıklama | 12,02 sn | 12,55 sn |

Her iki örnekte HTTPS 200, sistem DNS ve doğrudan UDP/DNS başarılıdır. Süreler düğmeye basılmasından itibaren ölçülür ve kullanıcı/UAC beklemesini içerir. Aynı koşullarda çoklu A/B dağılımı değildir; genel hızlanma veya başarısızlık oranı kanıtlamaz. İlk 0.7 denemesi Activity yeniden kullanım hatası nedeniyle 30 sn servis zaman aşımıyla başarısız oldu; ağ değiştirilmedi. Hata düzeltildikten sonra yukarıdaki soğuk başlangıç geçti.

Başarılı 0.7 örneğinde telefon algılama yaklaşık 227 ms, servis hazırlığı 165 ms, eşleştirme 95 ms idi. İlk başarıya kadar UI aşama kaydı 11,22 sn gösterdi; bağımsız denetleyicinin kontrolleri 11,46 sn içinde tamamlandı. Farklı gözlem noktalarıdır.

Film / Dizi profili telefon tarafından onaylandı ve seçili Wintun rotası aynı kaldı. Kayıtlı eşleştirmede yeni kod istenmedi. Gerçek Windows ve Android ekran görüntüleri yerel `build/reports` klasöründedir; kullanıcıya ait ham cihaz kayıtları depoya eklenmez.

## Test kapsamı ve sınırlar

Java derlemesi, Go vet/loopback motor testleri; Windows çevrimdışı işlem çökmesi, WFP ABI, sahiplik, eşzamanlı kurtarma; yedi eşleştirme kontrolü ve bağlantı regresyonları çalıştırılır. Canlı test aracı açık `--approved-physical-test` bayrağı ister ve seçili TetherMax rotası üzerinden gerçek HTTPS/DNS/UDP sonucu kaydeder.

Son paket kabul kayıtları ve kalan fiziksel senaryolar `docs/acceptance-0.7.0-tr.md` içinde ayrı tutulur. Uzun süreli kullanım, gerçek fiziksel kablo çıkar/tak ve istatistiksel başarı oranı tek bir kısa testle doğrulanmış sayılmaz. Operatör kota muhasebesi, MyPublicWiFi/TV ve anti-cheat uyumluluğu hakkında yeni iddia yoktur.

## Güncelleme ve geri dönüş

Paketler `build/artifacts/tethermax-android-0.7.0.apk` ve `tethermax-windows-x64-070.msi` olarak hazırlanır. APK mevcut paket üzerine aynı imzayla kurulur; uninstall/clear-data yapılmaz. Windows MSI UpgradeCode ve kullanıcı başına kurulum yolu korunur. DPAPI kimlikleri/tercihler kurulum bileşenlerine dahil edilmez.

Kaynak checkpoint: `checkpoint/0.6.0-1026e29`. Eski MSI, gerçekten kurulu telefondan alınan APK, şifreli Windows kimlik yedeği ve salt okunur ağ başlangıç raporu yerel `build/rollback-060-1026e29` içindedir.

Bağımsız SYSTEM kurtarma ön kontrolü canlı geçişten önce çıkış kodu 0 ile tamamlandı. Her native oturum ağ değişikliğinden önce kendi korumalı kurtarma kopyasını, SYSTEM görevini ve watchdog onayını hazırlar.

Manuel ağ kurtarma: önce TetherMax penceresini kapatın. Sonra yönetici PowerShell'de:

```powershell
& "$env:LOCALAPPDATA\Programs\TetherMax\TetherMax.Network.exe" recover-stale --approve-network-recovery
```

Araç etkin guardian varken yeni kurtarmayı reddeder; ilgisiz adaptörleri sıfırlamaz. Kurtarma kopyası/SYSTEM görevi sohbetten bağımsız çalışır. Kurtarma tamamlanmıyorsa yeni bağlantı başlatmayın; mevcut korumalı oturum kaydı üzerinden teşhis yapın.

Windows'ta 0.6 MSI daha düşük sürüm olduğu için önce yalnızca Windows TetherMax 0.7 paketini kaldırmak, sonra saklanan 0.6 MSI'yı kurmak gerekebilir. Kullanıcı kimlik dizinini silmeyin. Android uygulamasını kaldırmayın. `scripts/build-android-rollback.ps1`, checkpoint kaynaklarından aynı imzayla ileri `versionCode=8` taşıyan 0.6 geri dönüş APK'sı üretir. Bu paket hazırlanıp imzası doğrulanır, otomatik kurulmaz. Böylece Android sürüm düşürme engeline karşı anahtarları silmeden üzerine dönüş yolu vardır. Geri dönüş paketinden sonra tekrar 0.7 kurulacaksa yeni APK'nın sürüm kodu 8'den büyük olmalıdır.

## Son paket kabulü ve koordineli devir

Son paket Windows `0.7.0+f197610e2d26dac5ad25aa6c4559285504abef41` ve Android `0.7.0 / code 7` olarak kuruldu. Kurulu Windows uygulama/guardian DLL hash'leri ve telefondan tekrar çekilen APK hash'i teslim paketleriyle eşleşti. Go binary build bilgisi `vcs.revision=f197610`, `vcs.modified=false` gösterir. Şifreli Windows `identity.bin` dosyası başlangıç yedeğiyle birebir aynı kaldı.

Son kurulu pakette çift Bağlan tıklaması yalnızca bir deneme oluşturdu. Telefon 193 ms, servis hazırlığı 238 ms, eşleştirme 95 ms olarak gözlendi. Worker/Wintun hazır bildirimi 5,64 sn, ağ etkin bildirimi 11,05 sn, uygulamanın sağlıklı bağlantı bildirimi 11,63 sn idi. Bağımsız rota + sağlık denetimi 12,55 sn içinde tamamlandı. UAC beklemesi bu sürelerin içindedir.

Kullanıcı, canlı işlemlerin “Düzelt MyPublicWiFi TV paylaşımını” sohbetine devrini onayladı. Bu nedenle son kurulu paketin arayüzünden canlı profil seçimi ve sahip olunan forward kaybından otomatik toparlanma testleri bu sohbette başlatılmadı. Etkin bağlantı açık bırakıldı; diğer sohbetin paylaşım kaynak değişiklikleri 0.7 commitine veya paketlerine dahil edilmedi.

Kod commitinin GitHub Actions kontrolleri geçti:
- [Java/Go, APK ve Windows derlemeleri ile izole testler](https://github.com/CanKaya4/pdanetnobetci1/actions/runs/40075902201)
- [Ayrı Windows runner üzerinde gerçek Wintun/WFP ve çökme kurtarması](https://github.com/CanKaya4/pdanetnobetci1/actions/runs/40075898862)

Yerel APK mevcut kurulu imza anahtarıyla üretilmiştir. CI'da bağımsız üretilen geliştirme imzalı APK, bu cihazın güncelleme kimliğiyle aynı kabul edilmemelidir; kontrollü güncellemede hash'i kayıtlı yerel teslim APK'sı kullanılır.
