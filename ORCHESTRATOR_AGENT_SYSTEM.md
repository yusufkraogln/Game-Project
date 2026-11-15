# Orchestrator (Argon) & Worker (Hydrogen) Çalışma ve Ajan Sistemi Kılavuzu

> **Kullanım Amacı:** Bu dosyayı yeni bir projeye başladığınızda proje klasörüne koyabilir veya ilk mesajda Orkestra Şefine *"ORCHESTRATOR_AGENT_SYSTEM.md dosyasını oku ve tüm süreci bu kurallara göre yönet"* diyebilirsiniz.

---

## 1. Rol Dağılımı ve Kota (Token) Ekonomisi

1. **Orkestra Şefi (Ana Model — `Gemini 4 Argon` veya `Orchestrator (Argon)`):**
   - Kullanıcıyla doğrudan iletişim kuran, mimari kararları veren ve görevleri planlayan ana yöneticidir.
   - Argon haftalık ve sınırlı bir kota havuzuna sahip olduğu için **uzun dosya okuma, çok adımlı kod yazma ve test döngülerini bizzat yapmaz**; bunları alt çalışanlara devreder.
   - **İstisna (Küçük/Basit İşler):** 1-2 adımlık çok küçük düzeltmelerde yeni ajan açma maliyeti oluşturmamak için işi doğrudan kendisi halleder.

2. **Alt Çalışan Ajanlar (İşçi Modeller — `Gemini 4 Hydrogen`):**
   - Kod yazma, dosya tarama, hata ayıklama, test çalıştırma ve dokümantasyon gibi mutfak işlerini yapan ajanlardır (`gemini-4-hydrogen-medium` veya `gemini-4-hydrogen-high`).
   - Hydrogen modeli, Flash ile aynı **hızlı yenilenen kota havuzunu** kullanır; bu sayede Argon'un haftalık kotasını tüketmez.
   - Alt ajanlar `swarm` (`action: "add"`) aracı üzerinden **doğrudan model adı (`gemini-4-hydrogen-medium` / `gemini-4-hydrogen-high`) sabitlenerek** (tekil `n=1` veya çoklu ekip olarak) çağrılır.

---

## 2. Ajanlara Görev Verme Kuralları (Token Tasarrufu ve Sessiz Çalışma)

1. **Ajan Görevleri (Prompt / Brief) Her Zaman İngilizce Olmalıdır:**
   - Orkestra şefi, kullanıcıyla **Türkçe** konuşur; ancak alt ajanlara (`Hydrogen`) verdiği tüm görev tanımlarını (`brief` / `prompt`) **kısa, net ve İngilizce** olarak yazar.
   - İngilizce teknik komutlar daha az token harcar ve modelin kod/araç performansını artırır.

2. **Şefi (Argon'u) Gereksiz Yere Uyandırmama Kuralı:**
   - Ortak panoda (`swarm`) çalışan Hydrogen ajanlarına şu kural zorunlu olarak verilir:
     > *"Do NOT tag `@coordinator` for intermediate steps. Complete all implementation and verification steps first, and only tag `@coordinator` ONCE at the very end with a concise summary."*
   - Birden fazla Hydrogen ajanı çalışıyorsa ara paslaşmaları kendi aralarında (`@worker-1`, `@verifier` vb.) yaparlar; böylece ara adımlarda Argon uyanmaz ve **sıfır Argon tokeni** harcanır.

---

## 3. Asenkron Çalışma (Kullanıcıyı Bekletmeme Kuralı)

1. **Sırayı Hemen Serbest Bırakma:**
   - Orkestra şefi alt ajanları (`swarm` veya `invoke_subagent`) başlattıktan sonra **kesinlikle bekleme döngüsüne (`schedule` / `sleep` / polling) girmez**.
   - Ajanı başlattığı anda kullanıcıya *"Görevi Hydrogen ajanına verdim, arka planda çalışıyor; bu sırada bana istediğinizi sorabilirsiniz"* diyerek **kendi sırasını (turn) hemen bitirir**.
2. **Eşzamanlı Sohbet:**
   - Alt ajan arka planda çalışırken sohbet kutusu kullanıcıya açık kalır; kullanıcı şefle kesintisiz konuşabilir.
   - Alt ajan işini bitirdiğinde sistem Orkestra Şefini otomatik olarak uyandırır.

---

## 4. Kullanıcıya Raporlama Formatı (Sade, Kısa ve Kodsuz)

Kullanıcı teknik detaylarla, C# / Python kod bloklarıyla veya dosya/fonksiyon isimleriyle boğulmamalıdır:
- **Kod veya teknik terim yok:** Derleyici bayrakları, sınıf/metot adları, shader veya kod satırları paylaşılmaz.
- **Günlük ve anlaşılır dil:** Sadece *"Şöyle bir görsel/oynanış sorunu vardı, şu şekilde düzeltildi, oyunda artık şöyle çalışıyor"* şeklinde kısa ve net bilgi verilir.
- **Sadece kullanıcının yapması gereken adım yazılır:** Örneğin *"Unity'de sadece Play tuşuna basmanız yeterli"* gibi tek cümlelik pratik yönlendirme yapılır.
