# Teknik Mimari

Bu doküman, MVP’nin uygulanmasında geçerli teknik mimari kararları tanımlar. Domain kuralları için `docs/DomainModel.md`, ürün kapsamı için `docs/MVP.md`, repository çalışma kuralları için `AGENTS.md` esas alınır.

## 1. Mimari yaklaşım

Sistem modüler monolit olarak geliştirilecek ve tek bir ASP.NET Core API olarak deploy edilecektir.

Backend iki production projesinden oluşacaktır:

- `EducationPlatform.Domain`
- `EducationPlatform.Api`

MVP’de ayrı Application ve Infrastructure projeleri oluşturulmayacaktır. Domain projesi framework, HTTP, authentication, EF Core ve persistence ayrıntılarından mümkün olduğunca bağımsız kalacaktır.

API projesi şunları içerecektir:

- HTTP endpoint’leri
- Feature/use-case uygulama akışları
- EF Core persistence kodu
- Authentication ve authorization altyapısı
- Result–HTTP mapping
- Error handling
- Logging

Microservice mimarisi kullanılmayacaktır.

## 2. Repository yapısı

```text
education-platform/
├── AGENTS.md
├── docs/
│   ├── DomainModel.md
│   ├── MVP.md
│   └── Architecture.md
├── backend/
│   ├── src/
│   │   ├── EducationPlatform.Api/
│   │   └── EducationPlatform.Domain/
│   └── tests/
│       ├── EducationPlatform.Domain.Tests/
│       └── EducationPlatform.Api.Tests/
└── frontend/
    └── src/
        ├── app/
        ├── features/
        ├── pages/
        └── shared/
```

## 3. Backend organizasyonu

`EducationPlatform.Api` feature/use-case bazlı organize edilecektir.

```text
Features/
├── Auth/
├── Classrooms/
│   ├── CreateClassroom/
│   ├── AddStudent/
│   └── RemoveStudent/
├── Courses/
│   ├── CreateCourse/
│   ├── AddWeek/
│   └── PublishCourse/
├── Quizzes/
│   ├── StartAttempt/
│   └── CompleteAttempt/
└── Progress/
```

Bir kullanım senaryosunun endpoint, request/response modeli, request validation ve uygulama akışı mümkün olduğunca aynı feature alanında tutulacaktır.

- Her use case için zorunlu interface oluşturulmayacaktır.
- Her şeyi yöneten büyük Service sınıfları oluşturulmayacaktır.
- CQRS ve MediatR kullanılmayacaktır.
- Command/Query marker interface’leri ve gereksiz handler pipeline’ları oluşturulmayacaktır.

## 4. Domain sınırı

`EducationPlatform.Domain` şu domain yapılarını içerecektir:

- Classroom
- Course
- CourseWeek
- WeekContent
- Quiz
- Question
- Option
- QuizAttempt
- ContentProgress

MVP’de Domain projesinde ayrı bir User aggregate veya entity bulunmayacaktır. Kullanıcı hesapları API/persistence tarafında `ApplicationUser : IdentityUser<Guid>` ile yönetilecek; domain aggregate’leri kullanıcıları `Guid` kimlikleriyle referanslayacaktır. Kullanıcının varlığı ve Teacher/Student rolü application/use-case seviyesinde doğrulanacak, Domain projesi ASP.NET Core Identity’ye bağımlı olmayacaktır. Gerçek domain davranışları gerektiren bir User modeline ihtiyaç doğarsa bu karar yeniden değerlendirilebilir.

Aggregate sınırları ve iş kuralları `docs/DomainModel.md` kararlarına göre uygulanacaktır. Domain katmanı:

- ASP.NET Core’a bağımlı olmayacaktır.
- HTTP kavramlarını bilmeyecektir.
- EF Core veya DbContext’e bağımlı olmayacaktır.
- React veya frontend ayrıntılarını bilmeyecektir.

Her zaman korunması gereken iş kuralları mümkün olduğunca domain davranışları tarafından korunacaktır.

## 5. Veri erişimi

PostgreSQL ve Entity Framework Core kullanılacaktır. EF Core DbContext, feature/use-case kodunda doğrudan kullanılabilecektir.

Aşağıdaki yapılar oluşturulmayacaktır:

- Generic Repository
- `IRepository<T>`
- Generic CRUD service
- Ayrı Unit of Work abstraction
- Specification Pattern
- Her entity için repository

DbContext, EF Core’un repository ve Unit of Work davranışları için doğrudan kullanılacaktır. Okuma işlemlerinde response DTO’larına doğrudan projection yapılabilir. Çok adımlı yazma işlemleri gerektiğinde transaction içinde tamamlanacaktır.

Kritik benzersizlik kuralları, domain/application kontrollerine ek olarak uygun database constraint’leriyle desteklenecektir. İleride belirli bir aggregate için gerçekten karmaşık persistence ihtiyacı doğarsa yalnızca o aggregate’e özel repository değerlendirilebilir.

## 6. Result Pattern

Use-case işlemleri standart Result yaklaşımını kullanacaktır:

- Değer döndürmeyen işlem: `Result`
- Değer döndüren işlem: `Result<T>`

Beklenen business/application hataları exception yerine Result failure olarak temsil edilecektir. Örnek hata kodları:

- `classroom_not_found`
- `course_not_found`
- `student_not_found`
- `forbidden`
- `duplicate_membership`
- `course_not_publishable`
- `quiz_locked`
- `quiz_not_accessible`
- `invalid_operation`

Failure en az kararlı bir error code ve gerekli açıklamayı taşıyacaktır. Result yapısı karmaşık error hierarchy veya gereksiz generic abstraction’larla büyütülmeyecektir. Her iş kuralı için ayrı exception sınıfı oluşturulmayacaktır.

Beklenmeyen teknik hatalar Result’a çevrilmeyecek; global exception handling tarafından ele alınacaktır.

## 7. Result ve HTTP mapping

API katmanı Result değerlerini aşağıdaki HTTP yanıtlarına dönüştürecektir:

| Sonuç | HTTP yanıtı |
|---|---|
| Başarılı | `200 OK`, `201 Created` veya `204 No Content` |
| Validation failure | `400 Bad Request` |
| Authentication failure | `401 Unauthorized` |
| Authorization failure | `403 Forbidden` |
| Not found | `404 Not Found` |
| Business/state conflict | `409 Conflict` |

Başarısız API yanıtlarında ASP.NET Core ProblemDetails kullanılacaktır. Beklenmeyen teknik exception loglanacak, ayrıntısı istemciye gönderilmeyecek ve `500` ProblemDetails yanıtına dönüştürülecektir.

## 8. Authentication

ASP.NET Core Identity şu ihtiyaçlar için kullanılacaktır:

- Kullanıcı adı ve parola
- Güvenli password hashing
- Teacher ve Student rolleri
- Parola değiştirme
- Hesap kilitleme
- Teacher tarafından Student hesabı oluşturma

Kullanıcı persistence modeli `ApplicationUser : IdentityUser<Guid>` olacaktır. MVP’de bunun karşılığında Domain projesinde ayrı bir User modeli veya aggregate’i oluşturulmayacaktır.

Authentication, cookie yerine JWT kullanacaktır. JWT yapısı kısa ömürlü access token ve daha uzun ömürlü refresh token yaklaşımından oluşacaktır.

Refresh token:

- Güvenli biçimde saklanacaktır.
- Revoke edilebilecektir.
- Gerektiğinde rotation destekleyebilecek şekilde tasarlanacaktır.

IdentityServer, ayrı OAuth server veya ayrı authentication microservice kullanılmayacaktır. React, access token’ı API isteklerinde Bearer token olarak gönderecektir:

```http
Authorization: Bearer <access-token>
```

## 9. JWT güvenliği

JWT yalnızca gerekli minimum claim’leri taşıyacaktır:

- UserId
- Role

Gereksiz kişisel bilgi token’a eklenmeyecektir. Refresh token değerleri düz metin olarak kalıcı tutulmayacak; logout ve güvenlik durumlarında revoke edilebilecektir.

Token doğrulamasında issuer, audience, expiration ve signing key kontrolleri yapılacaktır. JWT secret veya signing key source control’e yazılmayacak; configuration/environment secret mekanizmasıyla yönetilecektir.

## 10. Authorization

Rol kontrolüne ek olarak resource-level authorization uygulanacaktır.

Teacher:

- Yalnızca kendi Classroom’larını yönetebilir.
- Yalnızca kendi Course’larını yönetebilir.
- Yalnızca kendi Classroom bağlamındaki Student sonuçlarını görebilir.

Student’ın Course erişimi için:

- Course `Published` olmalıdır.
- Course en az bir Classroom üzerinden Student’a atanmış olmalıdır.
- Student ilgili Classroom’da aktif üyeliğe sahip olmalıdır.

Teacher endpoint’leri Teacher, Student endpoint’leri Student rolü gerektirir. Authorization yalnızca React tarafına bırakılmayacak; API her istekte gerekli rol ve kaynak yetkisini doğrulayacaktır.

## 11. Validation

Validation üç seviyeye ayrılacaktır.

### Domain validation

Aggregate’in her koşulda koruması gereken kuralları kapsar:

- Course publish kuralları
- CourseWeek sıralaması
- WeekContent geçerliliği
- Quiz’de en az bir Question bulunması
- Question’da en az iki ve tam olarak bir doğru Option bulunması
- Quiz’in ilk QuizAttempt başladıktan sonra değiştirilememesi
- Tamamlanan QuizAttempt’ın değiştirilememesi

### Application/use-case validation

Kullanıcıya ve işlem bağlamına bağlı kuralları kapsar:

- Teacher’ın Course veya Classroom sahibi olması
- Student’ın Course’a erişebilmesi
- Student’ın aynı Classroom’a zaten eklenmiş olması
- Course’un Teacher’ın kendi Classroom’una atanması
- Quiz ile WeekContent ilişkisinin uyumlu olması

### API request validation

Request biçimine ilişkin zorunlu alan, boş değer, string uzunluğu, URL formatı, enum geçerliliği ve request formatı kontrollerini kapsar.

MVP başlangıcında FluentValidation kullanılmayacaktır. Data Annotations ve basit manuel validation yeterlidir. Aynı kural farklı katmanlarda gereksiz şekilde tekrarlanmayacaktır.

## 12. Error handling

Global exception handling kullanılacak ve ASP.NET Core ProblemDetails standart hata modeli olacaktır.

- Beklenen application/business hataları Result üzerinden taşınacaktır.
- Beklenmeyen teknik exception’lar global exception handler tarafından yakalanacaktır.
- Beklenmeyen exception Error seviyesinde tek kez loglanacaktır.
- Stack trace istemciye gönderilmeyecektir.
- İstemciye `500` ProblemDetails yanıtı verilecektir.

Aynı exception’ın tek request sırasında farklı katmanlarda tekrar tekrar loglanmasından kaçınılacaktır.

## 13. Logging

Logging, beklenmeyen hata ve teknik sorunların incelenmesine odaklanacaktır. ASP.NET Core `ILogger` abstraction’ı, Serilog implementation’ı ve merkezi log görüntüleme için Seq kullanılacaktır.

### Loglanacak durumlar

Error seviyesinde özellikle şunlar loglanacaktır:

- Beklenmeyen exception’lar
- Database işlemi hataları
- Authentication altyapısındaki beklenmeyen teknik hatalar
- JWT üretme veya doğrulama sırasındaki beklenmeyen teknik hatalar
- Harici servislerin teknik entegrasyon hataları
- İşlemi engelleyen diğer beklenmeyen teknik hatalar

Critical seviyesi yalnızca uygulamanın çalışmasını sürdüremediği ciddi sistem hatalarında kullanılacaktır.

### Loglanmayacak durumlar

Normal başarılı business işlemleri, başarılı HTTP request’leri ve başarılı database sorguları application loguna yazılmayacaktır. Validation, not found, forbidden, duplicate membership, quiz locked ve course not publishable gibi beklenen Result failure’lar Error seviyesinde loglanmayacaktır.

### Exception bağlamı

Global exception handler mümkün olduğunda şu structured alanları tek log kaydında taşıyacaktır:

- TraceId/CorrelationId
- Request path
- HTTP method
- Mevcutsa UserId
- Mevcutsa ilgili resource id
- Exception tipi, mesajı ve stack trace

Message template kullanılacak; structured logging avantajını kaybettiren string interpolation’dan kaçınılacaktır.

### Hassas veri güvenliği

Şunlar hiçbir durumda loglanmayacaktır:

- Password ve password hash
- JWT access token ve refresh token
- Authorization header ve cookie değerleri
- Secret, API key ve connection string
- Öğrencinin Quiz cevaplarının tamamı
- Gereksiz kişisel öğrenci bilgileri

Production’da başarılı SQL sorguları ayrıntılı loglanmayacak ve EF Core sensitive data logging kapalı olacaktır. Development’ta EF Core log seviyesi gerektiğinde geçici olarak artırılabilir.

Serilog logları Seq’e gönderilecektir. Seq’in kullanılamaması uygulamayı kullanılamaz hale getirmemelidir. Log retention, uygulama kodu yerine Seq/hosting konfigürasyonunda yönetilecektir.

MVP’de ayrı Audit Log sistemi olmayacaktır; business değişiklik geçmişi application error loglarıyla tutulmayacaktır.

## 14. Frontend

Frontend React ve TypeScript ile feature-based organize edilecektir.

```text
frontend/src/
├── app/
├── features/
│   ├── auth/
│   ├── classrooms/
│   ├── courses/
│   ├── quizzes/
│   └── progress/
├── pages/
└── shared/
    ├── api/
    ├── components/
    └── utilities/
```

- Redux veya başka global state management kütüphanesi MVP başlangıcında kullanılmayacaktır.
- Ekran ve form state’i React local state ile tutulacaktır.
- Gerçekten ortak küçük state için Context kullanılabilir.
- Server verisi gereksiz şekilde global client state’e kopyalanmayacaktır.
- API çağrıları component’lere dağılmayacaktır.
- Feature’lar kendi typed request/response fonksiyonlarını barındırabilir.

Ortak HTTP client; API base configuration, JWT access token gönderimi, ProblemDetails parsing ve `401` yönetimini merkezi olarak ele alacaktır. Server state karmaşıklaştığında TanStack Query değerlendirilebilir.

## 15. Test stratejisi

Testler `docs/MVP.md` kabul kriterlerine odaklanacaktır. Backend test projeleri:

- `EducationPlatform.Domain.Tests`
- `EducationPlatform.Api.Tests`

### Domain testleri

Hızlı ve database’den bağımsız unit testler özellikle şu davranışları doğrulayacaktır:

- Course publish kuralları
- Quiz geçerliliği
- Question/Option kuralları
- Quiz’in ilk attempt sonrasında kilitlenmesi
- Tamamlanan QuizAttempt değişmezliği
- Quiz puan hesaplama

### API/integration testleri

Gerçek PostgreSQL davranışını mümkün olduğunca doğrulayan integration testleri özellikle şunları kapsayacaktır:

- Teacher’ın Classroom oluşturması
- Student membership
- Resource authorization
- Draft/Published Course görünürlüğü
- Course publish validation
- QuizAttempt başlatma ve tamamlama
- ContentProgress benzersizliği
- Erişim kaldırıldığında geçmiş verilerin korunması
- Quiz raporlarında ilk, son ve en iyi puan

EF Core repository mock’ları kullanılmayacak, yüzde 100 code coverage hedeflenmeyecek ve gereksiz sayıda benzer test oluşturulmayacaktır.

## 16. Database migration yaklaşımı

EF Core migrations kullanılacak ve migration dosyaları source control altında tutulacaktır.

- Her anlamlı schema değişikliği ayrı ve açıklayıcı adlı bir migration olmalıdır.
- Production’a uygulanmış migration silinmemeli, yeniden yazılmamalı veya değiştirilmemelidir.
- Production schema değişiklikleri yeni migration ile yapılmalıdır.
- Destructive değişikliklerden önce veri etkisi, migration riski ve geri dönüş yaklaşımı değerlendirilmelidir.
- Production migration’ları application startup sırasında kontrolsüz biçimde otomatik uygulanmamalıdır.
- Migration’lar deployment sürecinin kontrollü bir adımı olarak uygulanmalıdır.

## 17. Dependency kuralları

- Domain projesi API projesine bağımlı olamaz.
- API projesi Domain projesine bağımlı olabilir.
- Frontend, backend implementation ayrıntılarına bağımlı olmamalıdır.
- Yeni dependency yalnızca somut ihtiyaç olduğunda eklenmelidir.

MVP’de şu bağımlılık ve pattern’ler eklenmeyecektir:

- MediatR
- CQRS framework
- Generic Repository
- Unit of Work abstraction
- FluentValidation
- Redux
- Event bus
- Message broker
- Redis
- Kubernetes
- Event sourcing

## 18. MVP dışında bırakılan teknik yapılar

MVP’de kullanılmayacaktır:

- Microservices
- Event sourcing
- Event bus
- Message broker
- Redis
- Kubernetes
- CQRS
- MediatR
- Generic Repository
- Unit of Work abstraction
- Ayrı Application projesi
- Ayrı Infrastructure projesi
- Ayrı Identity Server
- Kapsamlı Audit Log
- Distributed caching
- Background distributed processing

## 19. Gelecekte yeniden değerlendirilebilecek kararlar

Mimari kararlar şu ihtiyaçlar gerçekten ortaya çıktığında yeniden değerlendirilebilir:

- Mobil uygulama veya harici API istemcileri
- Birden fazla backend instance
- Belirgin biçimde büyüyen application/use-case kodu
- Artan harici servis entegrasyonları
- Frontend’de karmaşıklaşan server state yönetimi
- Merkezi cache ihtiyacı
- Artan background processing ihtiyacı
- Kurumsal audit trail gereksinimi
- Backend modüllerini bağımsız geliştiren ayrı ekipler

Bu ihtiyaçlar oluşmadan mimari önceden karmaşıklaştırılmayacaktır.

## 20. Mimari özet

```text
React + TypeScript
        |
        | HTTPS + JWT Bearer Token
        v
ASP.NET Core API
        |
        |-- Feature / Use Case yapısı
        |-- Result / Result<T>
        |-- ASP.NET Core Identity
        |-- JWT + Refresh Token
        |-- ProblemDetails
        |-- Global Exception Handling
        |-- Serilog
        |-- Seq
        |-- EF Core
        |
        v
PostgreSQL

Ayrı Domain projesi:

EducationPlatform.Domain
        |
        |-- Classroom
        |-- Course / CourseWeek / WeekContent
        |-- Quiz / Question / Option
        |-- QuizAttempt
        |-- ContentProgress

Test projeleri:

EducationPlatform.Domain.Tests
EducationPlatform.Api.Tests
```

Logging yalnızca beklenmeyen hata ve teknik problemlere odaklanacaktır. Başarılı business işlemleri application loglarına yazılmayacaktır.
