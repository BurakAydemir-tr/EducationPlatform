# Domain Modeli

## 1. Projenin domain özeti

Bu proje, ortaokul bilişim teknolojileri dersleri için geliştirilecek küçük ve genişletilebilir bir eğitim platformudur.

Platformun MVP kapsamındaki temel amaçları şunlardır:

- Öğretmenlerin sınıf oluşturması ve öğrenci eklemesi
- Öğretmenlerin haftalara ayrılmış dersler hazırlaması
- Haftalara konu anlatımı, video ve test eklenmesi
- Derslerin bir veya daha fazla sınıfa atanması
- Öğrencilerin kendilerine açık haftalık içerikleri takip etmesi
- Öğrencilerin test çözmesi
- Öğretmenlerin öğrenci ilerlemesini ve test sonuçlarını görmesi

Domain modeli kapsamlı bir LMS oluşturmayı hedeflemez. Yalnızca bu temel davranışları destekleyen entity’leri ve iş kurallarını içerir.

## 2. Temel entity’ler

### Kullanıcı hesabı ve kimlik referansları

MVP’de `EducationPlatform.Domain` içinde ayrı bir User aggregate veya entity bulunmaz. Kullanıcı hesabı, kimlik bilgileri ve Teacher/Student rolleri API/persistence tarafında ASP.NET Core Identity ile yönetilir.

Classroom, Course, QuizAttempt ve ContentProgress gibi domain modelleri kullanıcıları `Guid` kimlikleri üzerinden referanslar. Kullanıcının varlığı ve gerekli role sahip olması application/use-case seviyesinde doğrulanır. Domain projesi ASP.NET Core Identity’ye bağımlı değildir.

MVP’de öğretmen, öğrencinin adı ve sistem genelinde benzersiz kullanıcı adıyla öğrenci hesabı oluşturabilir. Sistem geçici giriş bilgisi oluşturabilir ve öğrenciden ilk girişte parolasını değiştirmesini isteyebilir. Bu hesap ve kimlik doğrulama davranışları domain modelinin dışındadır.

Kullanıcı adı ve öğrenci kodu aynı normalize edilmiş identifier alanını paylaşır. Bir kullanıcının kullanıcı adı, başka bir kullanıcının öğrenci koduyla çakışamaz; aynı kural ters yönde de geçerlidir.

### Classroom

Bir öğretmenin yönettiği öğrenci grubunu temsil eder.

Temel bilgiler:

- `Id`
- `Name`
- `TeacherId`

Bir Classroom’un tek bir sahibi öğretmen bulunur. Bir öğretmen birden fazla Classroom oluşturabilir.

Classroom, öğrenci kimliklerinden oluşan bir koleksiyon tutmaz ve öğrenci üyeliğini kendi davranışlarıyla yönetmez. Öğrenci–Classroom üyeliği application/use-case ve persistence seviyesinde yönetilir. İlişki persistence katmanında join tablosuyla tutulabilir; bunun için MVP’de ayrı bir `ClassroomMembership` domain entity’si bulunmaz.

### Course

Öğretmenin oluşturduğu dersi temsil eder.

Temel bilgiler:

- `Id`
- `Title`
- `Description`
- `TeacherId`
- `Status`
- Sınıf ilişkileri
- Haftalar

Course durumu yalnızca şu değerlerden biri olabilir:

- `Draft`
- `Published`

Bir Course birden fazla Classroom’a atanabilir. Course–Classroom ilişkisi persistence katmanında join tablosuyla tutulabilir. MVP’de ayrı bir `CourseAssignment` domain entity’si bulunmaz.

### CourseWeek

Course içindeki sıralı bir ders haftasını temsil eder.

Temel bilgiler:

- `Id`
- `Title`
- `Order`
- İçerikler

CourseWeek bağımsız bir aggregate root değildir. Course aggregate’i içinde yer alır ve Course üzerinden yönetilir.

### WeekContent

Bir CourseWeek içindeki sıralı içerik öğesini temsil eder.

Temel bilgiler:

- `Id`
- `Title`
- `Order`
- `Type`
- Türe özgü içerik verisi

Desteklenen içerik türleri:

- `Topic`
- `Video`
- `Quiz`

Topic türü konu anlatımı metni, Video türü video adresi ve isteğe bağlı açıklama taşır. Quiz türü ise içeriğe gömülü bir test taşımak yerine ayrı Quiz aggregate’ine `QuizId` ile referans verir.

### Quiz

Bir testi ve testin sorularını temsil eder.

Temel bilgiler:

- `Id`
- `Title`
- Sorular

Quiz ayrı bir aggregate root’tur. Question ve Option yapıları Quiz aggregate’i içinde bulunur.

MVP’de bir Quiz yalnızca bir Quiz türündeki WeekContent tarafından kullanılmalıdır. Bu kural erişim, ilerleme ve raporlama bağlamının belirsizleşmesini önler.

### Question

Quiz içindeki sıralı bir soruyu temsil eder.

Temel bilgiler:

- `Id`
- `Text`
- `Order`
- Seçenekler

Question, Quiz dışında bağımsız bir yaşam döngüsüne sahip değildir ve Quiz üzerinden yönetilir.

### Option

Question içindeki cevap seçeneğini temsil eder.

Temel bilgiler:

- `Id`
- `Text`
- `Order`
- `IsCorrect`

Option, Question ve Quiz dışında bağımsız olarak yönetilmez.

### QuizAttempt

Bir öğrencinin belirli bir Quiz üzerindeki tek çözüm denemesini temsil eder.

Temel bilgiler:

- `Id`
- `StudentId`
- `QuizId`
- `StartedAt`
- `CompletedAt`
- Öğrencinin cevapları
- `CorrectAnswerCount`
- `QuestionCount`
- `Score`

Her deneme doğrudan `QuizId` üzerinden Quiz’e bağlanır. Öğrenci cevapları QuizAttempt aggregate’inin parçası olarak tutulur.

### ContentProgress

Bir öğrencinin belirli bir WeekContent öğesini tamamladığını temsil eder.

Yalnızca şu bilgileri tutar:

- `StudentId`
- `ContentId`
- `CompletedAt`

`IsCompleted`, hafta yüzdesi veya ders yüzdesi gibi hesaplanabilir alanlar ContentProgress içinde tutulmaz.

## 3. Aggregate ve aggregate root sınırları

### Classroom aggregate

Aggregate root:

- `Classroom`

Sorumlulukları:

- Sınıf kimliğini ve adını yönetmek
- Sınıfın sahibi olan öğretmeni belirlemek

Classroom yalnızca `Id`, `Name` ve `TeacherId` state’ini taşır. Student kimliklerini veya User nesnelerini içine almaz. Öğrenci ekleme ve çıkarma Classroom aggregate davranışı değildir; bu işlemler application/use-case ve persistence seviyesinde yürütülür.

### Course aggregate

Aggregate root:

- `Course`

Aggregate içindeki yapılar:

- `CourseWeek`
- `WeekContent`

Sorumlulukları:

- Ders bilgilerini yönetmek
- Haftaları eklemek ve sıralamak
- Haftalara içerik eklemek ve sıralamak
- Course yayın kurallarını uygulamak
- Course’u Classroom’larla ilişkilendirmek

Quiz, Course aggregate’inin içinde bulunmaz. Quiz türündeki WeekContent yalnızca ilgili `QuizId` değerini taşır.

### Quiz aggregate

Aggregate root:

- `Quiz`

Aggregate içindeki yapılar:

- `Question`
- `Option`

Sorumlulukları:

- Soruları ve seçenekleri yönetmek
- Testin geçerlilik kurallarını uygulamak
- İlk öğrenci denemesi başladıktan sonra test içeriğinin değiştirilmesini engellemek

### QuizAttempt aggregate

Aggregate root:

- `QuizAttempt`

Öğrencinin cevapları ve hesaplanmış sonucu bu aggregate’in parçasıdır. Tamamlanmış QuizAttempt değiştirilemez.

### ContentProgress aggregate

Aggregate root:

- `ContentProgress`

Tek bir öğrenci–içerik tamamlanmasını temsil eder. Course aggregate’ine dahil edilmez; böylece öğrencilerin ilerleme işlemleri ders içeriğinin düzenlenmesiyle aynı işlem sınırını paylaşmaz.

## 4. Entity ilişkileri

Temel ilişkiler aşağıdaki gibidir:

```text
Teacher hesabı 1 ─── * Classroom
Teacher hesabı 1 ─── * Course

Classroom * ─── * Student hesabı
Course    * ─── * Classroom

Course 1 ─── * CourseWeek
CourseWeek 1 ─── * WeekContent

WeekContent(Type=Quiz) 1 ─── 1 Quiz
Quiz 1 ─── * Question
Question 1 ─── * Option

Student hesabı 1 ─── * QuizAttempt
Quiz 1 ─── * QuizAttempt

Student hesabı 1 ─── * ContentProgress
WeekContent 1 ─── * ContentProgress
```

Classroom–Student ve Course–Classroom çoktan çoğa ilişkileri persistence katmanında join tablolarıyla gerçekleştirilebilir. Classroom–Student üyeliği Classroom aggregate state’ine dahil değildir. Bu tabloların bulunması, ilişkilerin ayrı domain entity olması gerektiği anlamına gelmez.

## 5. Course yayın kuralları

- Yeni oluşturulan Course her zaman `Draft` durumundadır.
- Course yalnızca `Draft` veya `Published` durumunda olabilir.
- Draft Course öğrenciler tarafından görülemez.
- Published Course yalnızca atandığı Classroom’lardaki öğrenciler tarafından görülebilir.
- Published fakat hiçbir Classroom’a atanmamış Course öğrencilere görünmez.
- Bir Course aynı anda birden fazla Classroom’a atanabilir.
- Course en az bir CourseWeek içermeden Published yapılamaz.
- Published yapılırken her CourseWeek en az bir WeekContent içermelidir.
- Her WeekContent kendi türüne göre geçerli olmalıdır.
- Quiz türündeki WeekContent geçerli bir Quiz’e referans vermelidir.
- Referans verilen Quiz yayınlanabilir nitelikte geçerli sorular içermelidir.
- Ayrıntılı yayın zamanlaması veya içerik sürümleme sistemi bulunmaz.
- Draft Course içinde boş CourseWeek bulunabilir.
- Published Course içine daha sonra yeni hafta veya içerik eklenebilir.
- Yeni içerik eklenmesi öğrencilerin hesaplanan ilerleme yüzdesini düşürebilir.
- Published Course içindeki Topic ve Video içerikleri düzenlenebilir.
- Topic ve Video değişiklikleri öğrencilere hemen yansır.
- Topic ve Video için MVP’de değişiklik geçmişi tutulmaz.

## 6. Classroom ve öğrenci üyelik kuralları

- Classroom’u yalnızca Teacher rolündeki kullanıcı hesabı oluşturabilir.
- Her Classroom’un tek bir sahibi öğretmen bulunur.
- Öğretmen yalnızca sahibi olduğu Classroom’u yönetebilir.
- Classroom’a yalnızca Student rolündeki kullanıcı hesabı eklenebilir.
- Classroom öğrenci kimliklerinden oluşan bir koleksiyon tutmaz.
- Öğrenci ekleme ve çıkarma işlemleri Classroom aggregate davranışı değildir; application/use-case ve persistence seviyesinde yönetilir.
- Öğrencinin varlığı, Student rolünde olması ve işlemi yapan öğretmenin Classroom sahibi olması use-case seviyesinde doğrulanır.
- Aynı öğrenci aynı Classroom’da birden fazla aktif üyeliğe sahip olamaz.
- Duplicate aktif üyelik application kontrolü ve database unique constraint ile birlikte korunur.
- Bir öğrenci aynı anda birden fazla Classroom’da bulunabilir.
- Öğretmen mevcut öğrenciyi benzersiz kullanıcı adı veya öğrenci koduyla bulup sınıfa ekleyebilir.
- Öğretmenlerin sistemdeki bütün öğrencileri serbestçe listelemesi gerekmez.
- Aynı öğrenci için yeni hesap açılması yerine mümkünse mevcut kullanıcı hesabı kullanılmalıdır.
- Öğrenci Classroom’dan çıkarıldığında bu Classroom üzerinden sağlanan yeni içerik erişimini kaybeder.
- Öğrenci başka bir Classroom üzerinden aynı Course’a erişebiliyorsa Course erişimi devam eder.
- Öğrenci Classroom’dan çıkarıldığında geçmiş ContentProgress ve QuizAttempt kayıtları silinmez.
- Classroom–Student ilişkisi persistence katmanında join table ile tutulabilir.
- İleride üyelik geçmişi gerektiğinde `JoinedAt` ve `LeftAt` gibi bilgiler persistence modelinde tutulabilir. Bunun için ayrı bir domain entity gerekli değildir.

## 7. WeekContent kuralları

- Her WeekContent tam olarak bir CourseWeek’e aittir.
- WeekContent sırası CourseWeek içinde pozitif ve benzersiz olmalıdır.
- Aynı CourseWeek içinde birden fazla Topic, Video veya Quiz içeriği bulunabilir.
- Topic, Video ve Quiz içerikleri aynı sıralı koleksiyonda karışık biçimde yer alabilir.
- Topic türündeki içerik geçerli bir konu anlatımı metni taşımalıdır.
- Video türündeki içerik geçerli bir video adresi taşımalıdır.
- Quiz türündeki içerik geçerli bir `QuizId` taşımalıdır.
- Topic ve Video türündeki içerikler `QuizId` taşıyamaz.
- Quiz türündeki içerik ayrı Quiz aggregate’ine referans verir.
- Bir Quiz MVP’de birden fazla WeekContent tarafından paylaşılamaz.
- Öğrenci ilerlemesi WeekContent düzeyinde izlenir.
- Geçmiş ilerlemesi veya test sonucu bulunan WeekContent fiziksel olarak silinmemelidir.
- Böyle bir içerik kaldırılacaksa öğrenci görünümünden pasifleştirilir.
- Pasif içerikler güncel hafta ve ders ilerleme hesaplarının toplam içerik sayısına dahil edilmez.
- Geçmiş ContentProgress ve QuizAttempt kayıtları korunur.

## 8. Quiz ve Question kuralları

- Quiz ayrı bir aggregate root’tur.
- Question ve Option yalnızca Quiz aggregate’i üzerinden yönetilir.
- Quiz en az bir Question içermelidir.
- Question sırası Quiz içinde pozitif ve benzersiz olmalıdır.
- Her Question en az iki Option içermelidir.
- Her Question tam olarak bir doğru Option içermelidir.
- MVP’de yalnızca tek doğru cevaplı çoktan seçmeli sorular desteklenir.
- Option sırası Question içinde benzersiz olmalıdır.
- Quiz’e ilk QuizAttempt başladıktan sonra Quiz değiştirilemez.
- Kilitlenen Quiz üzerinde soru metni, seçenek, doğru cevap ve soru sırası değiştirilemez.
- Değişiklik gerektiğinde yeni bir Quiz oluşturulur ve ilgili WeekContent yeni `QuizId` değerine yönlendirilir.
- Eski Quiz ve ona bağlı QuizAttempt kayıtları korunur.
- Tam Quiz sürümleme sistemi bulunmaz.
- Soru bankası ve Quiz’ler arasında ortak Question kullanımı bulunmaz.

## 9. QuizAttempt kuralları

- QuizAttempt doğrudan `StudentId` ve `QuizId` üzerinden öğrenci ve Quiz ile ilişkilendirilir.
- Öğrenci yalnızca erişim hakkı bulunan bir Course içindeki Quiz için deneme başlatabilir.
- Öğrenci aynı Quiz’i sınırsız kez çözebilir.
- Her yeni çözüm ayrı bir QuizAttempt oluşturur.
- QuizAttempt başlatıldığında Quiz değiştirilemez hale gelir.
- Öğrencinin cevapları QuizAttempt aggregate’i içinde tutulur.
- Puan istemciden alınmaz; cevapların doğruluğundan sistem tarafından hesaplanır.
- Tamamlanmış QuizAttempt değiştirilemez.
- Tamamlanmamış deneme ilerleme ve başarı raporlarına dahil edilmez.
- Tamamlanmış bir QuizAttempt ilgili Quiz türündeki WeekContent’in tamamlanmasını sağlar.
- Birden fazla deneme yeni ContentProgress kayıtları oluşturmaz.
- QuizAttempt geçmişi hiçbir erişim veya üyelik değişikliğinde silinmez.
- Öğretmen raporunda aşağıdaki değerler QuizAttempt kayıtlarından hesaplanır:
  - Deneme sayısı
  - İlk puan
  - Son puan
  - En iyi puan
  - Son deneme tarihi
- Bu özet değerler ayrıca veritabanında saklanmaz.

## 10. ContentProgress kuralları

ContentProgress yalnızca şu alanları tutar:

- `StudentId`
- `ContentId`
- `CompletedAt`

Kurallar:

- Aynı `StudentId` ve `ContentId` çifti için en fazla bir ContentProgress kaydı bulunabilir.
- Kaydın bulunması içeriğin tamamlandığı anlamına gelir.
- `IsCompleted` alanı tutulmaz.
- Topic ve Video için tanımlanan tamamlanma eylemi ContentProgress oluşturur.
- Quiz için tamamlanmış QuizAttempt oluşması ContentProgress oluşturur.
- Öğrenci yalnızca erişebildiği bir WeekContent için ContentProgress oluşturabilir.
- Erişim kaybedildikten sonra mevcut ContentProgress kaydı korunur.
- ContentProgress kaydı Course, CourseWeek veya Classroom içine gömülmez.
- ContentProgress kaydı geçmiş tamamlama bilgisidir; öğrenci içeriği tekrar açtığında yeni kayıt oluşturulmaz.

Hafta ve ders ilerleme yüzdeleri saklanmaz.

Hafta ilerlemesi:

```text
Tamamlanan aktif hafta içeriklerinin sayısı
------------------------------------------------
Haftadaki toplam aktif WeekContent sayısı
```

Ders ilerlemesi:

```text
Tamamlanan aktif ders içeriklerinin sayısı
---------------------------------------------
Dersteki toplam aktif WeekContent sayısı
```

## 11. Öğrenci erişim kuralları

Bir öğrencinin Course’a erişebilmesi için aşağıdaki koşulların tamamı sağlanmalıdır:

- Course `Published` durumunda olmalıdır.
- Course en az bir Classroom’a atanmış olmalıdır.
- Öğrenci Course’un atandığı Classroom’lardan en az birinin aktif üyesi olmalıdır.

Ek kurallar:

- Draft Course hiçbir öğrenci tarafından görülemez.
- Öğrenci Course’a birden fazla Classroom üzerinden bağlı olabilir.
- Bir Classroom ataması kaldırıldığında diğer aktif Classroom atamaları erişimi sürdürebilir.
- Öğrenci yalnızca erişebildiği Course’un CourseWeek ve WeekContent öğelerini görebilir.
- Öğrenci yalnızca erişebildiği Quiz için QuizAttempt başlatabilir.
- Öğrenci yalnızca erişebildiği WeekContent için ContentProgress oluşturabilir.
- Öğrencinin sonradan erişimi kaybetmesi geçmiş kayıtlarını silmez.
- Course’un Classroom’dan kaldırılması o Classroom üzerinden yeni erişimi sona erdirir.
- Course–Classroom ilişkisinin kaldırılması geçmiş raporlamayı bozacak şekilde fiziksel veri kaybına neden olmamalıdır.

## 12. Geçmiş verilerin korunmasıyla ilgili kurallar

Aşağıdaki kayıtlar geçmiş eğitim verisi olarak kabul edilir:

- ContentProgress kayıtları
- Başlatılmış ve tamamlanmış QuizAttempt kayıtları
- QuizAttempt cevapları
- QuizAttempt sonuçları
- Deneme bulunan eski Quiz’ler

Kurallar:

- Öğrenci Classroom’dan çıkarıldığında geçmiş ilerleme ve test sonuçları korunur.
- Course Classroom’dan kaldırıldığında geçmiş ilerleme ve test sonuçları korunur.
- Öğrenci Course erişimini tamamen kaybetse bile geçmiş kayıtları silinmez.
- QuizAttempt geçmişi fiziksel olarak silinmez.
- Tamamlanmış QuizAttempt değiştirilemez.
- Deneme başlanmış Quiz değiştirilemez veya fiziksel olarak silinemez.
- Eski Quiz yerine yeni Quiz kullanılacaksa eski Quiz ve denemeleri korunur.
- Geçmiş ilerlemesi bulunan WeekContent fiziksel olarak silinmez; gerektiğinde pasifleştirilir.
- Üyelik ve Course ataması ilişkileri geçmiş raporları destekleyecek biçimde persistence katmanında pasifleştirilebilir veya tarihsel olarak korunabilir. Classroom–Student üyelik geçmişi için gerekirse `JoinedAt` ve `LeftAt` gibi persistence alanları kullanılabilir; bu bir domain entity gerektirmez.
- Öğretmen geçmiş sınıf raporlarında öğrencinin tamamlamalarını ve test sonuçlarını görebilmelidir.
- Geçmiş verilerin korunması, öğrencinin artık ilgili içeriğe erişim hakkı olduğu anlamına gelmez.

## 13. MVP dışında bırakılan özellikler

Aşağıdaki özellikler MVP domain modeline dahil değildir:

- Okul veya kurum hiyerarşisi
- Çoklu kurum desteği
- Akademik yıl ve dönem yönetimi
- Yönetici ve veli rolleri
- Yardımcı öğretmen desteği
- E-posta davet sistemi
- Toplu öğrenci içe aktarma
- Okul bilgi sistemi entegrasyonu
- Ödev ve dosya teslimi
- Mesajlaşma ve tartışma alanları
- Canlı ders
- Takvim ve bildirimler
- Sertifika, rozet ve oyunlaştırma
- Soru bankası
- Quiz’ler arasında ortak soru kullanımı
- Açık uçlu, eşleştirmeli veya birden fazla doğru cevaplı sorular
- Rastgele soru veya seçenek sıralaması
- Quiz süre sınırı
- Deneme sayısı sınırı
- Geçme notu
- Soru ağırlıkları
- Hafta bazlı yayınlama
- Zamanlanmış yayınlama
- Topic ve Video sürüm geçmişi
- Tam Quiz sürümleme sistemi
- Video izlenme yüzdesi ve kaldığı yer takibi
- SCORM, LTI veya başka LMS entegrasyonları
- Gelişmiş analitik ve veri ambarı
- İlerleme yüzdelerinin kalıcı olarak saklanması

## 14. İleride genişletilebilecek noktalar

Mevcut model aşağıdaki geliştirmelere engel olmayacak şekilde düzenlenmiştir:

- CourseWeek için ayrı yayın veya görünürlük durumu eklenebilir.
- Course için zamanlanmış yayınlama eklenebilir.
- Quiz’e isteğe bağlı deneme sınırı, süre veya geçme notu eklenebilir.
- Quiz kopyalama işlemi daha sonra resmi Quiz sürümleme mekanizmasına dönüştürülebilir.
- Quiz’in birden fazla WeekContent tarafından paylaşılması ileride desteklenebilir. Bu durumda erişim ve ContentProgress bağlamı ayrıca tanımlanmalıdır.
- Question yapısı daha sonra soru bankasına ayrılabilir.
- Yeni WeekContent türleri sıralı içerik modelini değiştirmeden eklenebilir.
- ContentProgress korunarak video izleme yüzdesi veya içerikte kalınan konum için ayrı etkileşim kayıtları eklenebilir.
- Classroom–Student persistence modeline `JoinedAt` ve `LeftAt` gibi katılma ve ayrılma bilgileri eklenebilir; Classroom aggregate’i öğrenci koleksiyonu taşımaya başlamaz.
- Course–Classroom ilişkisine atanma ve kaldırılma tarihleri eklenebilir.
- Öğrenci hesabı oluşturma süreci e-posta daveti, okul numarası veya merkezi kimlik sistemiyle genişletilebilir.
- Kullanıcı hesaplarına yeni roller authentication/application seviyesinde eklenebilir.
- Gerçek domain davranışları gerektiren bir kullanıcı modeli ihtiyacı ortaya çıkarsa ayrı bir User aggregate kararı yeniden değerlendirilebilir.
- Raporlar QuizAttempt ve ContentProgress kayıtlarından yeni metrikler üretecek biçimde genişletilebilir.
- Kullanım hacmi arttığında Quiz aggregate’i ve raporlama sorguları bağımsız depolama veya read model yapılarıyla ölçeklenebilir.

Bu genişleme noktaları MVP kapsamında yeni entity veya abstraction oluşturulmasını gerektirmez.
