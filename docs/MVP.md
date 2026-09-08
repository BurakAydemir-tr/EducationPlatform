# Minimum Uygulanabilir Ürün (MVP)

## 1. MVP amacı

MVP’nin amacı, öğretmenin haftalık ders içeriği hazırlayıp sınıflarına yayımlayabildiği; öğrencinin bu içerikleri takip edip quiz çözebildiği ve tarafların ilerlemeyi görebildiği ilk çalışan ürün sürümünü sunmaktır.

Bu sürüm kapsamlı bir öğrenme yönetim sistemi değildir. Başarı ölçütü, temel öğretmen–öğrenci öğrenme akışının baştan sona kullanılabilir olmasıdır.

## 2. MVP kullanıcı rolleri

MVP yalnızca iki kullanıcı rolü içerir:

- `Teacher`: Sınıfları, öğrencileri, ders içeriklerini ve öğrenci sonuçlarını yönetir.
- `Student`: Kendisine erişim verilmiş dersleri takip eder, içerikleri tamamlar ve quiz çözer.

## 3. Teacher yetenekleri

Teacher:

- Sisteme giriş yapabilir.
- Classroom oluşturabilir ve yalnızca kendi Classroom’larını yönetebilir.
- Benzersiz kullanıcı adıyla Student hesabı oluşturabilir.
- Mevcut bir Student’ı kullanıcı adı veya öğrenci koduyla bulup Classroom’a ekleyebilir.
- Student’ı Classroom’dan çıkarabilir.
- Course oluşturabilir ve `Draft` olarak hazırlayabilir.
- Course’a hafta ekleyebilir.
- Haftalara Topic, Video ve Quiz içerikleri ekleyebilir.
- Hafta ve WeekContent sıralarını değiştirebilir.
- Course’u bir veya birden fazla kendi Classroom’una atayabilir ya da atamasını kaldırabilir.
- Geçerli bir Draft Course’u `Published` yapabilir.
- Published Course içindeki Topic ve Video içeriklerini düzenleyebilir.
- Kendi Classroom’larındaki öğrencilerin hafta ve ders ilerlemelerini görebilir.
- Quiz sonuçlarını öğrenci bazında inceleyebilir.
- Öğrenci bazında deneme sayısı, ilk puan, son puan ve en iyi puanı görebilir.

## 4. Student yetenekleri

Student:

- Sisteme giriş yapabilir.
- Üyesi olduğu Classroom’lara atanmış Published Course’ları görebilir.
- Erişebildiği Course’un haftalarını ve aktif içeriklerini sırasıyla görebilir.
- Topic içeriğini açabilir ve tamamlayabilir.
- Video içeriğini açabilir ve tamamlayabilir.
- Quiz çözebilir ve sonucunu görebilir.
- Aynı Quiz’i sınırsız kez tekrar çözebilir.
- Kendi hafta ilerlemesini görebilir.
- Kendi ders ilerlemesini görebilir.

## 5. Temel kullanıcı akışları

### Sınıf ve öğrenci hazırlama

1. Teacher giriş yapar ve bir Classroom oluşturur.
2. Teacher yeni bir Student hesabı oluşturur veya mevcut Student’ı kullanıcı adı ya da öğrenci koduyla bulur.
3. Teacher Student’ı Classroom’a ekler.
4. Student kendisine verilen bilgilerle giriş yapar.

### Ders hazırlama ve yayımlama

1. Teacher bir Course oluşturur; Course `Draft` durumunda başlar.
2. Teacher Course’a haftalar ekler.
3. Teacher haftalara Topic, Video ve Quiz içerikleri ekler ve sıralar.
4. Teacher Course’u bir veya daha fazla Classroom’a atar.
5. Sistem yayın koşullarını doğrular.
6. Teacher Course’u `Published` yapar.

### Öğrencinin dersi tamamlaması

1. Student giriş yaptığında erişebildiği Published Course’ları görür.
2. Student Course haftalarını ve WeekContent öğelerini sırasıyla açar.
3. Topic ve Video tamamlandığında ilgili ContentProgress kaydı oluşur.
4. Student Quiz’i tamamladığında sonucu görür; QuizAttempt tamamlanır ve ilgili Quiz WeekContent için ContentProgress oluşur.
5. Hafta ve ders ilerlemesi mevcut aktif içerikler ile ContentProgress kayıtlarından hesaplanarak gösterilir.

### Öğretmenin sonuçları takip etmesi

1. Teacher kendi Classroom ve Course bağlamında öğrencileri görüntüler.
2. Teacher öğrencilerin tamamladığı içerikleri ve hesaplanan ilerlemelerini görür.
3. Teacher Quiz bazında deneme sayısını, ilk puanı, son puanı ve en iyi puanı görür.

## 6. Fonksiyonel gereksinimler

### Kimlik ve yetki

- Sistem Teacher ve Student kullanıcılarının giriş yapmasını sağlamalıdır.
- Kullanıcı yalnızca rolüne ve sahip olduğu erişime uygun işlemleri yapabilmelidir.
- Teacher yalnızca kendisine ait Classroom ve Course’ları yönetebilmelidir.

### Classroom ve üyelik

- Teacher Classroom oluşturabilmelidir.
- Teacher yeni Student hesabı oluşturabilmelidir.
- Teacher mevcut Student’ı Classroom’a ekleyebilmelidir.
- Aynı Student aynı Classroom’a ikinci kez eklenememelidir.
- Student aynı anda birden fazla Classroom’da bulunabilmelidir.
- Teacher Student’ı Classroom’dan çıkarabilmelidir.

### Course ve içerik

- Teacher tarafından oluşturulan Course başlangıçta `Draft` olmalıdır.
- Teacher Course’a haftalar ve haftalara sıralı WeekContent öğeleri ekleyebilmelidir.
- WeekContent türleri Topic, Video veya Quiz olmalıdır.
- Quiz türündeki WeekContent ayrı bir Quiz’e referans vermelidir.
- Teacher hafta ve içerik sıralarını değiştirebilmelidir.
- Aynı Course birden fazla Classroom’a atanabilmelidir.
- Yalnızca yayın koşullarını sağlayan Course `Published` yapılabilmelidir.
- Published Topic ve Video içerikleri düzenlenebilmeli ve değişiklikler öğrenciye yansımalıdır.

### Quiz ve denemeler

- Quiz ayrı bir aggregate root olmalı; Question ve Option yapıları Quiz aggregate’i içinde kalmalıdır.
- Quiz, Question ve her Question’a ait Option’ları içermelidir.
- Her Question en az iki Option ve tam olarak bir doğru Option içermelidir.
- Student yalnızca erişebildiği Course içindeki Quiz’i çözebilmelidir.
- QuizAttempt doğrudan Student ve Quiz ile ilişkilendirilmelidir.
- Puan, verilen cevaplardan sistem tarafından hesaplanmalıdır.
- Student aynı Quiz için sınırsız sayıda QuizAttempt başlatabilmelidir.
- İlk QuizAttempt başladıktan sonra ilgili Quiz değiştirilememelidir.
- Tamamlanan QuizAttempt sonradan değiştirilememelidir.

### İlerleme ve raporlama

- Tamamlanan her WeekContent için öğrenci başına en fazla bir ContentProgress bulunmalıdır.
- ContentProgress yalnızca `StudentId`, `ContentId` ve `CompletedAt` bilgilerini taşımalıdır.
- Hafta ve ders ilerleme yüzdeleri saklanmamalı; aktif WeekContent ve ContentProgress kayıtlarından hesaplanmalıdır.
- Teacher, kendi öğrencilerinin ilerleme ve Quiz sonuçlarını görebilmelidir.
- Quiz raporundaki deneme sayısı, ilk puan, son puan ve en iyi puan QuizAttempt geçmişinden hesaplanmalıdır.

### Erişim ve geçmiş

- Draft Course Student tarafından görüntülenememelidir.
- Published Course yalnızca atandığı Classroom’lardan en az birine aktif üyeliği bulunan Student tarafından görüntülenebilmelidir.
- Classroom veya Course ataması üzerinden erişim kaldırıldığında yeni erişim sona ermelidir.
- Erişim kaybı geçmiş ContentProgress ve QuizAttempt kayıtlarını silmemelidir.

## 7. Kabul kriterleri

### Teacher ve Classroom

- Teacher geçerli bilgilerle giriş yaptığında Teacher alanına erişebilmelidir.
- Teacher bir Classroom oluşturduğunda Classroom kendi hesabına ait olmalıdır.
- Teacher başka bir Teacher’a ait Classroom’u yönetememelidir.
- Teacher benzersiz kullanıcı adıyla Student hesabı oluşturabilmelidir; kullanılan kullanıcı adıyla ikinci hesap oluşturulamamalıdır.
- Bir kullanıcının kullanıcı adı başka bir kullanıcının öğrenci koduyla, öğrenci kodu da başka bir kullanıcının kullanıcı adıyla çakışmamalıdır.
- Teacher mevcut Student’ı kullanıcı adı veya öğrenci koduyla bulup kendi Classroom’una ekleyebilmelidir.
- Aynı Student aynı Classroom’a ikinci kez eklendiğinde yinelenen üyelik oluşmamalıdır.
- Student farklı Classroom’lara eklenebilmelidir.
- Teacher Student’ı Classroom’dan çıkardığında Student o üyelikten doğan yeni erişimini kaybetmelidir.

### Course ve yayınlama

- Teacher Course oluşturduğunda Course `Draft` olmalıdır.
- Draft Course hiçbir Student tarafından görüntülenememelidir.
- Teacher Draft Course’a boş hafta ekleyebilmelidir.
- Course Published olmadan önce en az bir CourseWeek içermelidir.
- Published Course içindeki her CourseWeek en az bir geçerli WeekContent içermelidir.
- Geçersiz Topic, Video veya Quiz içeren Course Published yapılamamalıdır.
- Teacher aynı Course’u birden fazla Classroom’a atayabilmelidir.
- Published Course yalnızca atandığı Classroom’lardaki erişimi olan Student’lara görünmelidir.
- Teacher Published Topic veya Video içeriğini düzenlediğinde Student güncel içeriği görmelidir.
- Teacher hafta veya içerik sırasını değiştirdiğinde Student güncel sıralamayı görmelidir.

### Quiz

- Quiz türündeki WeekContent geçerli bir Quiz’e bağlı olmalıdır.
- Geçerli Quiz en az bir Question içermelidir.
- Her Question en az iki Option ve tam olarak bir doğru Option içermelidir.
- Student erişemediği Course içindeki Quiz için QuizAttempt başlatamamalıdır.
- Student Quiz’i başlattığında QuizAttempt ilgili Student ve Quiz’e bağlı olmalıdır.
- İlk QuizAttempt başladıktan sonra Quiz soruları, seçenekleri, doğru cevapları ve sırası değiştirilememelidir.
- Student Quiz’i tamamladığında QuizAttempt tamamlanmış ve puanı cevaplardan hesaplanmış olmalıdır.
- Student tamamladığı Quiz’in sonucunu görebilmelidir.
- Student aynı Quiz’i tekrar çözdüğünde ayrı bir QuizAttempt oluşmalıdır.
- Tamamlanan QuizAttempt sonradan değiştirilememelidir.

### İlerleme ve raporlar

- Student yalnızca erişebildiği Course içindeki WeekContent için ContentProgress oluşturabilmelidir.
- Topic veya Video tamamlandığında ilgili Student ve WeekContent için ContentProgress oluşmalıdır.
- Tamamlanan Quiz ilgili Quiz WeekContent için ContentProgress oluşturmalıdır.
- Aynı içerik için aynı Student adına ikinci ContentProgress kaydı oluşmamalıdır.
- ContentProgress yalnızca StudentId, ContentId ve CompletedAt bilgilerini içermelidir.
- Hafta ilerlemesi, haftadaki aktif WeekContent ve Student’ın ContentProgress kayıtlarından hesaplanmalıdır.
- Ders ilerlemesi, dersteki aktif WeekContent ve Student’ın ContentProgress kayıtlarından hesaplanmalıdır.
- İlerleme yüzdeleri ayrı bir kalıcı değer olarak tutulmamalıdır.
- Yeni aktif içerik eklendiğinde gösterilen ilerleme yeni içerik toplamına göre yeniden hesaplanmalıdır.
- Teacher öğrenci bazında Quiz deneme sayısını, ilk puanı, son puanı ve en iyi puanı görebilmelidir.

### Erişim kaybı ve geçmiş veriler

- Student Classroom’dan çıkarıldığında geçmiş ContentProgress ve QuizAttempt kayıtları korunmalıdır.
- Course Classroom’dan kaldırıldığında geçmiş ContentProgress ve QuizAttempt kayıtları korunmalıdır.
- Student aynı Course’a başka bir aktif Classroom üzerinden erişiyorsa erişimi devam etmelidir.
- Student tüm erişimini kaybettiğinde yeni içerik görüntüleyememeli veya yeni QuizAttempt başlatamamalıdır.
- Erişim kaybından sonra Teacher’ın geçmiş ilerleme ve Quiz sonuçlarını görüntüleyebilmesi sürmelidir.

## 8. MVP dışında bırakılan özellikler

- Yapay zekâ ile içerik üretimi
- Mobil uygulama
- Veli rolü
- Admin paneli
- Okul veya kurum yönetimi
- Mesajlaşma
- Bildirim sistemi
- Canlı ders
- Ödev teslimi
- Sertifika
- Oyunlaştırma
- Rozet veya puan sistemi
- Soru bankası
- Çoklu soru tipleri
- Gelişmiş analitik
- Video izlenme yüzdesi
- Akademik yıl yönetimi
- Takvim veya yayın zamanlama
- Quiz süre limiti
- Quiz deneme limiti
- Quiz soru veya seçenek randomization
- Dosya yükleme
- Öğretmenler arası içerik paylaşımı

## 9. MVP tamamlanmış sayılma kriterleri

MVP aşağıdaki kullanıcı sonuçlarının tamamı sağlandığında tamamlanmış kabul edilir:

- Teacher giriş yapıp bir Classroom oluşturabilir, yeni veya mevcut Student’ı bu Classroom’a ekleyebilir ve gerektiğinde çıkarabilir.
- Teacher haftalara ayrılmış; Topic, Video ve Quiz içeren bir Course hazırlayabilir, sıralayabilir, bir veya daha fazla Classroom’a atayabilir ve Published yapabilir.
- Draft Course Student’a görünmez; yetkili Student Published Course’u ve haftalık içeriklerini doğru sırada görebilir.
- Student Topic ve Video içeriklerini tamamlayabilir, Quiz çözebilir, sonucunu görebilir ve Quiz’i tekrar çözebilir.
- Student kendi hafta ve ders ilerlemesini güncel içeriklere göre görebilir.
- Teacher öğrencilerin ilerlemesini ve Quiz bazında deneme sayısı, ilk puan, son puan ve en iyi puanını görebilir.
- Student’ın Classroom üyeliği veya Course erişimi kaldırıldığında yeni erişim sona erer; geçmiş ilerleme ve Quiz sonuçları korunur.
- Temel Teacher ve Student akışları, MVP dışında bırakılan özelliklere ihtiyaç duymadan baştan sona tamamlanabilir.
