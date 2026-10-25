# TetherMax RC10 — Stability & Performance

25 Ekim 2026. Android: `1.0.0-rc.10-stability`, versionCode 21.
Windows: `1.0.0-rc.10`, MSI ProductVersion `1.0.10`, x64 ve kendi .NET runtime'ı.
Bu sürüm önizlemedir. APK önceki geliştirme sertifikasıyla imzalanır;
MSI/EXE için Authenticode ürün imzası yoktur.

## Somut hata ve düzeltmeler

**Windows kapanışı.** Salt okunur Windows olay kayıtlarında 25 Ekim 13:04 ve
13:24'te `Window.Show()` / `Window.VerifyNotClosing()` kaynaklı kapanmalar vardı.
Mevcut kapanış catch yolu da senkron temizleme hatasında aynı hatayı üretebiliyordu.
Temizleme artık `Closing` olayından `Task.Yield()` ile çıktıktan sonra yürür;
senkron hata enjekte edilince pencere güvenle açık kalır, ikinci onaylı çıkış
tamamlanır. Kapanan pencereye ikinci örnek/tepsi aktivasyonu da engellenir.
Tarihsel kayıtlar bu APK/MSI'nın fiziksel kabul testi sayılmaz.

**IPC ve süreç sahipliği.** Tek bozuk JSON/şema örneği, kanalın kopması olarak
işlenmez. Gerçek EOF/IO kaybı güvenli kurtarmaya gider. Komut yazmaları 3 saniye
sınırlandırılır; pipe yazıcısı kalp atışı sırasında başka görevden dispose
edilmez. Testin ortaya çıkardığı, pipe kapandıktan sonraki writer dispose hatası
beklenen kapanış hatası olarak ele alınır. Aynı oturumun eşzamanlı dispose
çağrıları aynı kurtarma görevini bekler. SOCKS motorunun stderr'i sürekli
boşaltılır; pipe dolup motoru kilitlemesi önlenir. Ham hata çıkışı kayda alınmaz.

**Yarın yeniden açılış.** Tercihler önce geçici dosyaya yazılıp `Flush(true)`
sonrasında atomik değiştirilir. Son geçerli JSON `.previous` olarak korunur;
yarım/bozuk ana dosya bu yedekten okunur. Mevcut DPAPI/Keystore eşleştirme,
hotspot parolası ve sahiplik kontrollü eski oturum kurtarması korunur. Önceki
Windows çalışma ağacı düzeltmelerinin gerekli kaynakları da paketle eşleşmesi
için bu dala dahil edildi; ayar/anahtar dosyaları yayınlanmadı.

**USB Guardian.** Aynı cihaz/yerel port/remote adıyla eşleşen kayıp forward,
`--no-rebind` ile aynı portta onarılır. Çalışan motorun portları değiştirilmez.
Yabancı eşleme yeniden bağlanmaz veya temizlenmez. Bir kayıp gözlem oturumu
sonlandırmaz; mevcut iki gözlemli debounce korunur. ADB sunucusu yeniden
başlatılmaz, `kill-server`, USB reset, root veya RSA sıfırlama kullanılmaz.

MSI, Google'ın resmi 37.0.1 ADB istemcisi, gerekli Windows kütüphaneleri ve
NOTICE dosyasını içerir; Android Studio gerektirmez. SDK istemcisi varsa mevcut
yol tercih edilir. Yerel sunucu sürümü salt okunur smart-socket sorgusuyla
kontrol edilir; uyumsuz/yanıtsız sunucu zorla öldürülmez. Sunucu yokken uygulama
açılışı otomatik sunucu başlatmaz; kullanıcı Bağlan'ı seçtiğinde veya zaten
başlatılmış oturumun onarımında hazırlanabilir. Telefonun USB hata ayıklama/RSA
izni ve Windows ADB sürücüsü hâlâ gerekir; sürücü kurucusu dağıtılmaz.

**Android IOException.** Tek akıştaki IOException/RuntimeException servis
ölçeğinde kapanmaya dönüşmez; hata sınıfı/sayacı kimlik veya bearer içermeden
gösterilir. EOF normal sonlandırma sayılır. Relay işçi havuzu 280 işçiyle
sınırlandırıldı ve 30 saniye boşta kalınca işçiler bırakılır. Gönderim reddinde
akış yuvası/soketi geri bırakılır. TCP yarım kapatma ve UDP sınırları korunur.
UDP tamponu yeniden kullanılır; receive uzunluğu her datagramda sıfırlanır.

Veri ve eşleştirme dinleyicileri ayrı kurtarılır. Eşleştirme accept hatasının
sürekli hızlı döngüye girmesi önlendi. 1/2/4/8/16/30 saniyelik denemelerden sonra
dakikada bir düşük sıklıkta deneme sürer; mevcut akışlar ve trust korunur. Kullanıcı
servisi durdurunca bekleme kesilir; servis kendini zorla yeniden başlatmaz.
Android ağ callback'i içinden senkron link sorgusu kaldırıldı; link özellikleri
ilgili callback'ten alınır. RC9 ekran kapalı CPU lease/termal koruması korunur.

**Bağlantı doğrulama.** İlk geçici HTTPS/DNS başarısızlığında hemen rota kaldırmak
yerine aynı adaptör/worker/rota üzerinde en fazla üç mevcut küçük doğrulama
partisi çalışır. Yanlış adaptör başarı sayılmaz; iptal hemen gözlenir. Mevcut
sınırlı tanılama modu ve veri bütçesi korunur. Bu normal bağlantıda üç hız testi
veya sürekli internet taraması değildir.

## Smart Network Performance

Mevcut host/flow adaleti, CoDel kuyruğu, Valorant süreç algılama ve manuel
profiller korunur. Önceden çalışma ağacında olan backlog ölçümü derlenen
pakete dahil edilip test edildi. Gerçek byte hızı + yerel backlog + gecikme
birlikte değerlendirilir; 5G simgesinden kapasite uydurulmaz.

Yeni düzeltmeler:

- Başarısız RTT örneği yük/gecikme histerezisini sıfırlar; kesintinin iki
  tarafındaki örnekler birleştirilip hız gereksiz yere düşürülmez.
- Hız azaltımı için **aynı yönde** üç sürekli yüksek gecikme/yük örneği gerekir;
  art arda upload/download patlamaları tek yönde sürekli tıkanma sayılmaz.
- Geçersiz veya zaman sırası bozuk örnek byte ölçüm penceresini tüketmez.
- Yavaş toparlanma mevcut açık kullanıcı tavanlarını korur ve dahili 10 Gbps
  üst sınırını aşmaz. Maksimum modun mevcut adaptif bypass davranışı korunur.

Adaptif ölçüm mevcut seçenek açıkken çalışır ve küçük DNS RTT ölçüm trafiği
üretir. DNS RTT, Valorant sunucu pingi değildir. UDP/QUIC şifreli video içeriği
tanınmış gibi gösterilmez; genel kuyruk adaleti uygulanır. ICS NAT sonrasında
TV'nin ayrı kimliği görünmeyebilir. Gerçek oyun pingi, video takılması veya
5G Mbps iyileşmesi bu sürüm için fiziksel olarak ölçülmedi.

## Doğrulama

| Test | Sonuç / sınır |
|---|---|
| Saf Java mobil politika/lifecycle | PASS; 100 restart, lease, histerezis, 1.000.000 grafik eklemesi |
| Relay hata enjeksiyonu | PASS; 200 IO/runtime hata, yuva sızıntısı yok, sonraki authenticated control başarılı |
| Java relay / Go istemcisi | PASS; yeni derlenen fixture ile gerçek loopback TCP/UDP/DNS/DoH ve protokol testleri |
| Go politika testleri | PASS; kapasite düşüşü, idle yönün korunması, outage, alternatif burst, bozuk örnek penceresi |
| Windows bağlantı/IPC | PASS; gerçek özel pipe, malformed sample sonrası active kalma, cancel/join, graceful stop, eşzamanlı disposal |
| ADB ön kontrolü | PASS; yalnızca rastgele loopback portunda sahte sunucu; compatible/foreign/truncated/missing; adb.exe çalıştırılmadı |
| WPF | PASS; senkron temizleme hatası, deferred exit, hide/reopen, 100 otomatik istekte elevation yok, tr/en/de çizim, tema/boyut |
| Windows kurtarma | PASS; fake backend + bağımsız süreç owner/stall/watcher crash/retry/executor; gerçek ağ mutasyonu yok |
| Eşleştirme / hotspot portal / probe | PASS; DPAPI, trust reset, loopback HTTP, nonce, bounded removal fixture |
| APK / MSI | Üretildi; APK imzası doğrulandı; MSI veritabanı salt okunur incelendi; kurulmadı |
| GitHub Linux race/vet | PASS; Linux üzerinde `go vet ./...`, `go test -race -count=1 -v ./...` ve yeni derlenen Java fixture |

Raporlar GitHub Release eklerinde; GitHub Actions sonucu kaynak dalındadır.
Masaüstü ana ekranı offline render ile de kontrol edildi.
[Başarılı cloud çalışması](https://github.com/CanKaya4/pdanetnobetci1/actions/runs/40221714288)
`f09bbf4` üzerindedir; ürün kodu `465dd33` ile aynıdır. İlk CI çalışmasının
cache dizini izin hatası workflow'da düzeltilmiştir; ürün testleri azaltılmamıştır.

## Korunan sınır ve fiziksel kabul

CELLULAR → uygulama soketleri → mevcut USB protokolü → Windows IP yolu korunur.
Tethering/APN/TTL/operatör hesabı/standart hotspot davranışı değiştirilmedi.
Hotspot kotasından muafiyet **doğrulanmadı ve garanti edilmez**.

Çalışan PdaNet, MyPublicWiFi, adaptör, DNS, rota, firewall veya sürücüler bu
geliştirme sırasında değiştirilmedi. APK/MSI kurulmadı; fiziksel ADB komutu veya
gerçek mobil veri testi çalıştırılmadı. `AGENTS.md` bu aşama için ayrı açık onay
gerektiriyor. Sonraki kabul: aynı cihazda ekran kilidi, kablo ayrılıp takılması,
hücresel kayıp/dönüş, uygulama/worker çökmesi ve 24–72 saat yük/idle döngüsü.

Kablo/ADB/işletim sistemi kapanmasıyla gerçekten kesilen TCP veya oyun
oturumunun aynen devamı garanti edilemez. OS journal kurtarması güvenliği
önceler; tam uygulama/worker kaybından sonra yeni yönetici onayı gerekiyorsa
kullanıcı Bağlan'ı seçer. Otomatik UAC döngüsü veya kalıcı yönetici servis kurulumu
eklenmedi. Bilinmeyen fatal UI hataları yutulmaz; tanı kaydı bırakılır.

## Resmi araştırma kaynakları

- [WPF Closing kısıtları](https://learn.microsoft.com/en-us/dotnet/api/system.windows.window.closing)
- [.NET görev sahipliği ve hata gözleme](https://learn.microsoft.com/en-us/dotnet/standard/asynchronous-programming-patterns/keep-async-methods-alive)
- [İptal edilebilir StreamWriter yazması](https://learn.microsoft.com/en-us/dotnet/api/system.io.streamwriter.writelineasync)
- [Android foreground servis hata ayıklama](https://developer.android.com/develop/background-work/services/fgs/troubleshooting)
- [Android NetworkCallback yarış uyarıları](https://developer.android.com/reference/android/net/ConnectivityManager.NetworkCallback)
- [ADB 37.0.1 sürüm notları](https://developer.android.com/tools/releases/platform-tools)
- [AOSP ADB sunucu protokolü](https://android.googlesource.com/platform/packages/modules/adb/)
- [RFC 8290 FQ-CoDel ve düşük hacimli akışlar](https://www.rfc-editor.org/rfc/rfc8290.html)

Yeni büyük transport/protokol veya ağ arka ucu değişimi yerine mevcut doğrulanmış
yolda arıza izolasyonu ve kuyruk ölçüm doğruluğu tercih edildi.
