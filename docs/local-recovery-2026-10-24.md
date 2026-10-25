# Yerel kurtarma düzeltmesi — 24 Ekim 2026

Bu çalışma günlük kullanıma hazır ürün teslimatı değildir. Canlı Wintun geçişi,
hotspot kapalı kullanım ve operatör sayaç karşılaştırması yapılmadı.

## Değişiklikler

- Ağ değişikliklerinden önce SYSTEM hesabında zamanlanmış kurtarma görevi kurulur.
  Çalıştırılabilir dosyalar oturumun yalnızca yönetici/SYSTEM yazılabilir klasörüne
  kopyalanır. Görevin gerçekten çalıştığına ilişkin kayıt gelmezse aktivasyon durur.
- Görev açılışta ve dakikada bir çalışır. Guardian yaşamıyorsa veya heartbeat 15
  saniyeden eskiyse sahip olunan oturumu temizler; tamamlanınca görevini kaldırır.
  Guardian ve watcher birlikte çökerse kurtarma için yaklaşık bir dakikalık
  zamanlama gecikmesi olabilir. Gerçek yeniden başlatma henüz sınanmadı.
- Atomik dosya yayınlama sırasında Windows sharing/lock violation için sınırlı
  yeniden okuma eklendi; diğer hatalar gizlenmez.
- Global mutex aynı iş parçacığında alınır ve bırakılır; async devamın farklı
  iş parçacığında kilidi bırakma hatası giderildi.

## Bu bilgisayarda yapılan kontroller

- NetworkTests: işlem ölümü, heartbeat durması, watcher ölümü, geçici kurtarma
  hatası, OS yorumlayıcısının durdurulması ve sharing lock kontrolleri geçti.
  Bunlar ağ cmdletlerini taklit eder; canlı aktarım kanıtı değildir.
- `recovery-self-test --approve-network-recovery` yönetici/UAC ile çalıştırıldı.
  Gerçek SYSTEM görevi korunan kopyadan başladı; artık yaşamayan süreçleri olan,
  adaptör/rota/DNS/WFP filtresi hiç kurulmamış oturumu native backend ile temizledi.
  Exit 0. Kayıt: build/reports/recovery-system-preflight.txt.
  Bu kontrol kurulu aktif WFP/rota durumundan kurtarma kanıtı değildir.
- ADB Galaxy A56 SM-A566B cihazını yetkili gördü ve mevcut Android TunnelService
  foreground durumda bulundu. Telefonun hücresel/APN/hotspot ayarı değiştirilmedi.
- Ethernet 5/RNDIS Up durumunda kaldı; TetherMax canlı yönlendirmesi etkinleşmedi.

## Engellenen işlem

USB eşleştirmesi ve 120 saniye/1 MiB canlı sistem testi için çalıştırıcı oluşturup
çalıştıracak araç çağrısı otomatik onay denetimince `blocked by policy` ile
reddedildi. Ayrıntılı gerekçe verilmedi. Reddedilen işlem başka yoldan denenmedi.
Canlı test, sınırsız GUI oturumu ve hotspot-off geçişi başarılı kabul edilmedi.
Türk Telekom ve PdaNet sayaç davranışı ölçülmedi; sonuç belirsizdir.
