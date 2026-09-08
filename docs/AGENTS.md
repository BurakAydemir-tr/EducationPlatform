# Repository Geliştirme Kuralları

## Proje amacı

Bu proje, ortaokul bilişim teknolojileri dersleri için geliştirilen bir eğitim platformudur.

Öğretmenler sınıfları yönetir; haftalık Topic, Video ve Quiz içerikleri olan Course’lar hazırlar; öğrenci ilerlemelerini ve Quiz sonuçlarını takip eder. Öğrenciler kendilerine atanmış Course’ları takip eder, haftalık içerikleri tamamlar, Quiz çözer ve ilerlemelerini görür.

## Source of truth

Kararları aşağıdaki sırayla uygula:

1. `docs/DomainModel.md`: Domain ve business kuralları.
2. `docs/MVP.md`: MVP kapsamı ve kabul kriterleri.
3. `docs/Architecture.md`: Teknik mimari kararlar.
4. `docs/AGENTS.md`: Repository çalışma kuralları.

Bu belgeleri ilgili bir değişiklikten önce oku. Aralarında çelişki görürsen tahmin etme; çelişkiyi bildir. Bir görev DomainModel veya Architecture kararını değiştirmeyi gerektiriyorsa implementasyondan önce bunu ve gerekçesini açıkla, kullanıcıdan karar değişikliği iste.

## Çalışma yöntemi

Büyük veya birden fazla katmanı etkileyen değişikliklerde önce:

1. İlgili kodu incele.
2. Etkilenecek dosyaları belirle.
3. Mevcut pattern ve mimariyi belirle.
4. Değişiklik planını hazırla.
5. Riskleri belirt.

Kullanıcı açıkça implementasyon istemediyse kod değiştirme. Implementasyon sırasında yalnızca görev için gerekli değişiklikleri yap.

## Scope ve kod kalitesi

- İlgisiz kodu veya mevcut davranışı değiştirme.
- Gereksiz refactoring, özellik, abstraction veya dependency ekleme.
- Basit çözüm yeterliyse daha karmaşık mimari kullanma.
- Okunabilir, sürdürülebilir ve domain terminolojisiyle uyumlu kod yaz.
- İş kurallarını controller veya UI içine dağıtma.
- Aynı iş kuralını farklı yerlerde gereksiz tekrar etme.
- Validation’ı uygun katmanda tut.
- Hataları sessizce yutma.
- Güvenlik kontrollerini client tarafına bırakma.

## Backend mimarisi

- Backend modüler monolit olacaktır.
- Production backend projeleri yalnızca `EducationPlatform.Domain` ve `EducationPlatform.Api` olacaktır.
- Ayrı Application veya Infrastructure projesi oluşturma.
- Yeni proje veya layer eklemeden önce açık kullanıcı talimatı iste.
- Domain projesi API projesine bağımlı olamaz; API projesi Domain’e bağımlı olabilir.
- Domain’e ASP.NET Core, EF Core, HTTP veya authentication bağımlılığı ekleme.
- Application/use-case akışlarını API içindeki feature yapısında tut.
- Her şeyi yöneten büyük Service sınıfları ve gereksiz domain service’ler oluşturma.

## Feature ve use case organizasyonu

API kodunu feature/use-case bazlı düzenle. Bir feature’ın endpoint, request, response, request validation ve use-case akışını mümkün olduğunca aynı feature alanında tut.

- Her use case için zorunlu interface oluşturma.
- CQRS veya MediatR kullanma.
- Command/Query framework, marker interface veya handler pipeline oluşturma.

## Veri erişimi

- EF Core ve PostgreSQL kullan.
- DbContext’i feature/use-case kodunda doğrudan kullanabilirsin.
- Okuma sorgularında gerektiğinde response DTO projection kullan.
- Kritik benzersizlik kurallarını uygun database constraint ile destekle.
- Gerçek ve özel bir persistence ihtiyacı olmadan repository abstraction ekleme.
- Generic Repository, `IRepository<T>`, Generic CRUD service, ayrı Unit of Work abstraction veya Specification Pattern oluşturma.
- Her entity için repository oluşturma.

## Result Pattern ve HTTP hata yönetimi

- Use-case işlemlerinde `Result` veya `Result<T>` kullan.
- Beklenen business/application hatalarını kararlı error code ve gerekli açıklamayı taşıyan Result failure ile temsil et.
- Her hata için ayrı exception sınıfı oluşturma.
- Beklenmeyen teknik exception’ları Result’a dönüştürme.

API Result değerlerini genel olarak şöyle eşlemelidir:

- Validation: `400`
- Authentication: `401`
- Authorization: `403`
- Not found: `404`
- Business/state conflict: `409`

Failure response’larında ASP.NET Core ProblemDetails kullan. Beklenmeyen teknik exception’ları global exception handler’da ele al ve ayrıntılarını client’a gönderme.

## Authentication ve authorization

- ASP.NET Core Identity kullan.
- Authentication için cookie yerine JWT kullan.
- Kısa ömürlü access token ve daha uzun ömürlü refresh token yaklaşımını kullan.
- JWT secret/signing key’i kod veya source control içine yazma.
- Token’da yalnızca gerekli minimum claim’leri tut; gereksiz kişisel bilgi ekleme.
- Refresh token’ı düz metin olarak kalıcı tutma; güvenli sakla ve revoke edilebilir yap.
- Ayrı IdentityServer, OAuth server veya authentication microservice oluşturma.

Frontend yetki kontrollerine güvenme; API her istekte rol ve kaynak yetkisini doğrulamalıdır.

- Teacher yalnızca kendi Classroom ve Course kaynaklarını yönetebilir ve yetkili olduğu Student sonuçlarını görebilir.
- Student erişimini Course’un Published olması, Course–Classroom ataması ve aktif Classroom üyeliğiyle doğrula.
- Yalnızca role bakma; resource-level authorization uygula.

## Validation

Validation sorumluluklarını ayır:

- Domain: Her zaman korunması gereken business invariant’lar.
- Application/use-case: Kullanıcı ve mevcut bağlama bağlı kurallar.
- API request: Request biçimi ve taşıma validation’ı.

Aynı business kuralını katmanlarda gereksiz tekrar etme. FluentValidation ekleme; yalnızca açık talimatla yeniden değerlendir.

## Logging

- ASP.NET Core `ILogger` abstraction’ı ve Serilog kullan; logları Seq’e gönder.
- Logging’i beklenmeyen hata ve teknik problemlere odakla.
- Normal başarılı business işlemlerini, HTTP request’lerini veya SQL sorgularını loglama.
- Validation, not found, forbidden, duplicate membership, quiz locked ve course not publishable gibi beklenen Result failure’ları Error olarak loglama.
- Beklenmeyen exception’ı global exception handler seviyesinde yalnızca bir kez logla.
- Mümkünse TraceId/CorrelationId, request path, HTTP method, mevcut UserId, ilgili resource id ve exception bilgisini structured alanlar olarak ekle.
- String interpolation yerine message template kullan.
- Production’da EF Core sensitive data logging açma.

Şunları hiçbir durumda loglama:

- Password veya password hash
- JWT access token veya refresh token
- Authorization header veya cookie
- API key, secret veya connection string
- Öğrencinin tüm Quiz cevapları
- Gereksiz kişisel bilgiler

## Frontend

- React, TypeScript ve feature-based yapı kullan.
- Redux veya başlangıçta başka global state management kütüphanesi ekleme.
- Basit ekran/form state’i için React local state; gerçekten ortak küçük state için gerekirse Context kullan.
- API çağrılarını component’lere dağıtma; ortak HTTP client kullan.
- HTTP client JWT gönderimini, ProblemDetails parsing’i ve `401` yönetimini merkezi ele almalıdır.
- TanStack Query veya başka server-state kütüphanesini açık ihtiyaç ya da kullanıcı talimatı olmadan ekleme.
- Frontend’i backend implementation ayrıntılarına bağımlı yapma.

## Test ve doğrulama

Backend test projeleri `EducationPlatform.Domain.Tests` ve `EducationPlatform.Api.Tests` olacaktır.

- Testleri özellikle `docs/MVP.md` kabul kriterlerine göre oluştur.
- Domain business kuralları için database’den bağımsız unit test yaz.
- Kritik API akışlarında gerçek PostgreSQL davranışını doğrulayan integration test tercih et.
- EF Core’u repository mock’larıyla test etme.
- Yüzde 100 coverage hedefleme veya gereksiz benzer testler üretme.
- Business rule değiştiğinde ilgili regression testini değerlendir.
- Değişiklikten sonra mümkün olan en dar ilgili doğrulamayı ve testleri çalıştır.

Test başarısız olduğunda önce kök nedeni araştır; yalnızca değişiklikten kaynaklanan problemi düzelt ve testi sırf geçmesi için anlamsız biçimde değiştirme.

## Database ve migration güvenliği

- EF Core migrations kullan ve migration’ları source control altında tut.
- Production’a uygulanmış migration’ı silme, değiştirme veya yeniden yazma.
- Destructive schema değişikliğinden önce veri etkisini ve riski açıkla, kullanıcı izni iste.
- Production migration’larını application startup sırasında kontrolsüz otomatik çalıştırma.
- Gereksiz schema değişikliği veya veri silme yapma.

## Dependency ve mimari güvenliği

Yeni NuGet veya npm paketi eklemeden önce mevcut araçlarla çözümü kontrol et ve neden gerekli olduğunu açıkla.

Açık kullanıcı talimatı olmadan şunları ekleme veya oluşturma:

- MediatR, FluentValidation veya AutoMapper
- Redux
- Generic Repository veya Unit of Work abstraction
- Redis veya caching infrastructure
- Event bus veya message broker
- IdentityServer veya OpenIddict
- Microservice veya authentication microservice
- Event sourcing
- Kubernetes
- Ayrı Application veya Infrastructure projesi
- Kapsamlı Audit Log sistemi

## Git güvenliği

Açıkça istenmedikçe commit, push, merge veya rebase yapma; branch değiştirme veya silme; kullanıcı değişikliklerini discard etme; destructive Git komutları çalıştırma. Mevcut kullanıcı değişikliklerini koru.

## Görev tamamlama

Implementasyon tamamlandığında kısa şekilde şunları raporla:

1. Ne değişti.
2. Hangi önemli dosyalar değişti.
3. Hangi build, test ve validation komutları çalıştırıldı ve sonuçları.
4. Başarısız kontrol, kalan risk veya bilinmeyenler.

Commit veya push yapma.
