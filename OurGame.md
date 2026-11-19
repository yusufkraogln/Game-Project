# POLITICAL MAYHEM: Battle for the Cities
**Game Design Document — Version 0.4**

**Tür:** Multiplayer Political RTS / Strategy  
**Oyun motoru:** Unity 6 / C#  
**Multiplayer:** Photon Fusion 2  
**Yapay zekâ:** LLM + Custom MCP Server + RAG  
**Oyuncu sayısı:** 2  
**Şehir sayısı:** 6  
**Hedef maç süresi:** 15–18 dakika

---

## 1. Oyunun Genel Konsepti

Political Mayhem, iki oyuncunun kurgusal bir ülkede seçimleri kazanmak için siyasi mücadele verdiği, yapay zekâ destekli bir multiplayer strateji oyunudur.

Oyuncular siyasi konuşmalar yapar, rakiplerinin mitinglerini sabote eder ve farklı ideolojilere sahip seçmenleri ikna etmeye çalışır.

Oyuncular başlangıçta bir siyasi görüş seçmez. Yapay zekâ konuşmaları analiz ederek oyuncuların siyasi kimliklerini (Overall) zaman içerisinde oluşturur.

Başarılı konuşmalar, ideolojik yakınlık ve halkın ikna edilmesi oyunculara parti bağışı kazandırır.

**Toplanan bütün para seçim gecesine kadar birikir. Para yalnızca seçim gecesinde rüşvet yoluyla sınırlı miktarda oy avantajı elde etmek için kullanılabilir.**

Altı şehirdeki seçimlerin sonunda en fazla şehri kazanan oyuncu galip gelir.

## 2. Şehirler ve Siyasi Gruplar

Oyunda nüfusları sabit altı şehir bulunur.

| Şehir | Nüfus |
|---|---:|
| Başkent | 120.000 |
| Limankent | 90.000 |
| Demirkent | 110.000 |
| Yeşilova | 80.000 |
| Üniversitekent | 70.000 |
| Dağşehir | 60.000 |

Şehirlerin konumları ve nüfusları değişmez.

Her şehirde beş siyasi görüş bulunur:

1. Milliyetçiler
2. Liberaller
3. Demokratlar
4. Komünistler
5. Emperyalistler

Her maçın başında şehirlerdeki bu görüşlerin nüfus oranları rastgele oluşturulur.

Örneğin Demirkent'te bir maçta milliyetçiler %40, liberaller %20 olabilirken sonraki maçta bu oranlar değişebilir.

Her şehrin toplam ideolojik dağılımı %100'dür.

Şehirler ayrı bina inşası gerektirmeyen stratejik merkezler olarak görev yapar.

## 3. Overall — Dinamik Siyasi Kimlik

Oyuncuların siyasi görüşleri önceden seçilmez.

Her konuşmanın ardından LLM, konuşmanın hangi ideolojilere ne kadar yakın olduğunu belirler.

Oyuncuların beş farklı Overall değeri bulunur:

- Milliyetçilik
- Liberalizm
- Demokratlık
- Komünizm
- Emperyalizm

Her değer 0–100 arasında tutulur ve oyuncunun kendi içinde bağımsızdır.

**Overall güncelleme formülü:**

Yeni Overall = %80 × Önceki Overall + %20 × Konuşma İdeolojisi

Overall, geleneksel RTS oyunlarındaki teknoloji ve gelişim sistemine benzer şekilde çalışır.

Belirli ideolojik yakınlık eşiklerine ulaşmak oyuncuya pasif avantajlar sağlayabilir.

Overall'ın etkilediği özellikler:

- Şehirdeki başlangıç seyirci tepkileri
- Konuşmaların ikna gücü
- Sabotajcıların ikna başarısı
- Toplanan bağış miktarı
- Final seçimlerindeki ideolojik uyum

Oyuncular yalnızca kendi Overall değerlerini kesin olarak görebilir.

## 4. Miting Turlarıı

Oyunda altı miting turu bulunur.

Her oyuncu üç kez konuşmacı, üç kez sabotajcı olur.

| Tur | Konuşmacı | Sabotajcı |
|---|---|---|
| 1 | Oyuncu A | Oyuncu B |
| 2 | Oyuncu B | Oyuncu A |
| 3 | Oyuncu A | Oyuncu B |
| 4 | Oyuncu B | Oyuncu A |
| 5 | Oyuncu A | Oyuncu B |
| 6 | Oyuncu B | Oyuncu A |

Her tur başlamadan iki oyuncu miting yapılacak şehre oy verir.

Oylar eşitse aday gösterilen şehirlerden biri rastgele seçilir.

Daha önce miting yapılan şehirler tekrar seçilemez.

Her turun akışı:

1. Şehir seçimi — 15 saniye
2. Hazırlık — 30 saniye
3. Konuşma ve sabotaj — 60 saniye
4. AI değerlendirmesi — 10 saniye

## 5. Konuşma Mekaniği

Konuşmacının 60 saniyesi bulunur.

Oyuncu mikrofondan veya metin yoluyla konuşabilir.

LLM konuşmayı analiz ederek:

- Argüman kalitesini
- İkna gücünü
- İdeolojik yakınlığını
- Siyasi tutarlılığını
- Farklı siyasi gruplar üzerindeki etkisini

değerlendirir.

Konuşmanın sonucu oyuncunun Overall gelişimini ve bağış miktarını etkiler.

Seyirciler, oyuncunun Overall'ı ile kendi ideolojik görüşleri arasındaki yakınlığa göre alkışlayabilir, yuhalayabilir, protesto edebilir veya mizahi biçimde nesneler fırlatabilir.

Konuşma sırasında seyircilerin fikri değişebilir.

## 6. Sabotaj Mekaniği

Konuşmacı konuşurken rakip oyuncu kalabalığın arasına girebilir.

**Sözlü sabotaj:**

Rakibin argümanlarına karşı konuşarak seyircileri ikna etmeye çalışır. LLM hangi tarafın argümanlarının daha etkili olduğunu belirler.

Sözlü sabotajın doğrudan cezası bulunmaz.

**Fiziksel sabotaj:**

Oyuncu seyirciler arasında yumruk atarak kavga çıkarabilir.

Bu durum konuşmacının miting performansına ağır zarar verebilir.

Ancak beş kişilik güvenlik ekibi sabotajcıyı yakalamaya çalışır.

### Revolver mekaniği

Sabotajcı yakalanırsa miting sonunda beş şüphelinin arasına yerleştirilir.

Konuşmacı isterse bir şüpheliyi revolver ile vurabilir.

Doğru kişiyi seçerse sabotajcının sıradaki konuşma hakkı bloke edilir.

Yanlış kişiyi seçerse ağır siyasi itibar kaybı yaşar.

Sahne grafik şiddet içermeyen karikatürize bir politik komedi tarzında hazırlanır.

## 7. Parti Bütçesi ve Bağış Ekonomisi

Oyunda **tek parasal kaynak parti bütçesidir.**

Para yalnızca başarılı mitingler sonucunda gelen bağışlardan elde edilir.

### Bağış sistemi

Bağış miktarı şu faktörlere göre hesaplanır:

- Şehrin ideolojik nüfus dağılımı
- Oyuncunun Overall'ı
- Konuşmanın gücü
- Seyircilerin ikna edilme derecesi
- Sabotajın etkisi

Her ideolojik grubun bağış potansiyeli ayrı hesaplanır.

**Bağış = Grup Nüfusu × Bağış Eğilimi × Konuşma Etkisi × Overall Uyumu × Ölçek Katsayısı**

LLM değerlendirme puanları üretir; bağış tutarını Unity'nin deterministik hesaplama sistemi belirler.

### Paranın kullanım amacı

**Para oyun boyunca harcanmayacak.**

Oyuncular mitinglerde bağış kazanarak para biriktirecek.

Parti bütçesi yalnızca finaldeki seçim gecesinde kullanılabilecek.

Bu bütçe şehirlerde sınırlı oy avantajı elde etmek için rüşvete harcanabilecek.

Oyuncuların para biriktirme başarısı seçim gecesindeki stratejik seçeneklerini artıracak.

## 8. RTS ve Gerçek Zamanlı Kontrol

Oyunun ana stratejik birimleri şehirler ve kampanya ekipleridir.

Klasik bina inşa sistemi bulunmaz; şehirler mevcut stratejik merkezler olarak kullanılır.

Oyuncuların kontrol edebileceği üç temel birim:

- Kampanya Gönüllüsü
- Propagandacı
- Koruma

Birimler şehirlerde hareket edebilir, gruplandırılabilir ve rakip birimlerle gerçek zamanlı mücadele edebilir.

Temel savaş mekaniğinde can puanı, hasar, hedef seçimi ve birimin yenilmesi bulunur.

Her oyuncuya eşit başlangıç birimleri sağlanır. Birimler para harcanmadan, oyun kurallarıyla tanımlanan tur ve kapasite sınırlarına göre görevlendirilebilir.

Böylece parti bütçesi yalnızca seçim gecesine saklanır.

Şehirler ve siyasi Overall, oyuncuların stratejik gelişim ve kontrol sistemini oluşturur.

## 9. Fog of War — Bilgi Gizliliği

Oyuncular rakiplerinin bazı özel bilgilerini göremez.

Gizli bilgiler:

- Rakibin parti bütçesi
- Rakibin kesin Overall puanları
- Rakibin özel stratejik planları
- Henüz açıklanmayan seçim destek oranları

Rakibin kamuya açık konuşmaları ve hareketleri görülebilir.

AI Strategic Commander yalnızca oyuncunun erişmeye yetkili olduğu bilgilere ulaşabilir.

## 10. Tur Sonu Sonuç Ekranı

Her mitingin ardından oyuncuya iki temel sonuç gösterilir.

**Siyasi Overall:** Oyuncunun güncel beş ideolojik yakınlık puanı.

**Speech Effectiveness:** Konuşmasının 0–100 arasındaki etkililik puanı.

Kazanılan bağış miktarı oyuncunun parti bütçesine otomatik eklenir.

Şehirdeki kesin seçmen desteği ve rakibin gerçek durumu gizli kalır.

Asıl sonuç yalnızca seçim gecesinde açıklanır.

## 11. Seçim Gecesi

Altı miting tamamlandığında seçim gecesi başlar.

Şehirler tek tek oy kullanır.

Her şehir için üç aşama vardır.

### Aşama 1 — Son dakika konuşması

İki oyuncuya da 15 saniye konuşma hakkı verilir.

Oyuncular rakiplerini eleştirebilir, vaatlerini hatırlatabilir ve seçmenleri son kez ikna etmeye çalışabilir.

LLM bu konuşmaların etkisini değerlendirir.

### Aşama 2 — Rüşvet

Her iki oyuncu mevcut parti bütçelerinden o şehir için istedikleri miktarı ayırabilir.

Rüşvet, oyunun kurgusal seçim manipülasyonu mekaniğidir.

- Rüşvet vermek isteğe bağlıdır.
- İki oyuncu kararlarını gizli ve eşzamanlı verir.
- Harcanan para parti bütçesinden düşülür.
- Harcanan tutar geri alınamaz.
- Daha fazla para daha yüksek etki sağlayabilir ancak azalan getiri uygulanır.
- Rüşvetin net etkisi şehirdeki oy oranını en fazla **2 yüzde puanı** değiştirebilir.
- İki taraf da para harcarsa etkileri birbirine karşı hesaplanır.

Rüşvet tek başına büyük bir seçimi kazandıracak kadar güçlü olmayacak. Ancak birbirine çok yakın sonuçlarda kritik rol oynayabilecek.

Örneğin bir oyuncu normal şartlarda %49 oy alacakken, daha fazla rüşvet harcaması sonucunda %50,5'e ulaşarak şehri kazanabilir.

Oyuncular hangi şehirlerin kritik olabileceğini tahmin edip bütçelerini altı seçim arasında dağıtmak zorunda kalacak.

### Aşama 3 — Oyların hesaplanması

İlk olarak her siyasi grubun oyunculara vereceği destek hesaplanır.

| Faktör | Ağırlık |
|---|---:|
| Overall uyumu | %30 |
| Miting konuşması | %30 |
| Kampanya faaliyetleri | %15 |
| Sabotaj etkisi | %10 |
| Son dakika konuşması | %15 |

**Temel Destek = 0.30 × Overall + 0.30 × Konuşma + 0.15 × Kampanya + 0.10 × Sabotaj + 0.15 × Final Konuşması**

İki adayın temel destek puanlarından şehirdeki başlangıç oy payları hesaplanır.

Ardından seçim gecesinde harcanan rüşvetlerin sınırlı etkisi eklenir.

Rüşvet, toplam oy sayısını artırmaz. Oyların yalnızca iki aday arasındaki dağılımını sınırlı ölçüde değiştirir.

Bütün hesaplamalar Photon host tarafından deterministik biçimde gerçekleştirilir.

LLM oy sayısını doğrudan belirleyemez.

### Şehir sonuçları

Her şehirde seçim sonucu açıklanır.

Örnek:

**DEMİRKENT — 110.000 OY**

Oyuncu A: %50,8  
Oyuncu B: %49,2

Kazanan oyuncunun siyasi rengi şehir haritasına uygulanır.

Ardından sıradaki şehre geçilir.

## 12. Kazanma Şartı

Altı şehirdeki seçimlerin sonunda en fazla şehri kazanan oyuncu galip gelir.

Sonuç 3–3 olursa toplam halk oyuna bakılır.

Toplam halk oyu da eşitse önceden belirlenmiş eşitlik bozma kuralı uygulanır.

## 13. Balkon Konuşması

Kazanan oyuncu 60 saniyelik balkon konuşması yapar.

Oyuncu ne söylerse söylesin seyirciler aşırı coşkuyla alkışlar.

Mizahi sloganlar, abartılı hayranlık tepkileri, konfeti ve kutlama animasyonları gösterilir.

Balkon konuşmasıyla maç sona erer.

## 14. AI ve MCP Mimarisi

Oyunda iki temel AI görevi vardır.

**Speech Evaluation AI:** Konuşmaları analiz eder, Overall yakınlıklarını, ikna puanlarını ve sabotaj etkisini belirler.

**AI Strategic Commander:** Oyuncunun sorularını anlayarak canlı oyun durumunu MCP araçlarıyla inceler, RAG üzerinden oyun kurallarını getirir ve stratejik öneriler üretir.

Önerilen MCP araçları:

1. `get_player_budget`
2. `get_player_overall`
3. `get_city_intelligence`
4. `get_owned_units`
5. `get_visible_opponent_activity`
6. `get_recent_events`
7. `estimate_bribery_impact`
8. `search_game_knowledge`

AI, gerektiğinde en az üç farklı canlı MCP aracını dinamik olarak kullanabilir.

RAG; şehir kuralları, ideolojik profiller, birimler, oyun ekonomisi, seçim mekanikleri ve güvenlik kurallarını içerir.

AI'ın önerdiği gerçek oyun eylemleri JSON ActionPlan biçiminde hazırlanır.

Oyuncu onaylamadan hiçbir etkili eylem uygulanmaz. Onaylanan eylemler Unity tarafından tekrar doğrulanır ve Photon host tarafından yürütülür.

AI gizli rakip bilgilerine erişemez veya oyun otoritesini değiştiremez.

## 15. Multiplayer ve Teknik Sistem

Photon Fusion 2 Host Mode kullanılacaktır.

Host şu bilgilerin otoritesi olacaktır:

- Şehirlerin ideolojik dağılımları
- Oyuncu bütçeleri
- Overall puanları
- Kampanya birimleri
- Sabotaj olayları
- Bağışlar
- Rüşvet harcamaları
- Gizli destek oranları
- Seçim sonuçları

LLM değerlendirmeleri sunucu üzerinden alınır ve doğrulanır.

Speech-to-text mikrofonla konuşmayı destekler; metin girişi alternatif olarak bulunur.

NPC kalabalıkları Unity'nin Crowd Reaction System sistemi tarafından yönetilir.

## 16. Test ve Teslim

AI testlerinde aşağıdaki metrikler ölçülecektir:

- Ortalama, medyan ve maksimum AI gecikmesi
- En az 10 soruda MCP araç seçme doğruluğu
- Geçersiz AI eylem oranı
- RAG retrieval doğruluğu
- Prompt-injection dayanıklılığı
- Gizli bilgi sızıntısının engellenmesi
- Multiplayer durum tutarlılığı

Final tesliminde Unity kaynak projesi, Photon entegrasyonu, MCP sunucusu, AI Agent, RAG bilgi tabanı, JSON şemaları, test logları, IEEE raporu, sunum ve Windows build bulunacaktır.

## 17. Akademik Uyumluluk Notu

Bu sürüm, hocanın RTS konseptine alternatif bir siyasi strateji yorumu sunmaktadır.

Ancak üç klasik RTS şartı farklı biçimde ele alınmıştır:

- Üç kaynak türü yerine tek para kaynağı.
- Üç bina türü yerine şehirler.
- Klasik fog-of-war yerine özel bilgilerin gizlenmesi.

Ayrıca birimlerin kaynak maliyetiyle eğitilmesi ve işçilerin kaynak toplayıp depolaması yerine bağış ekonomisi kullanılmıştır.

Dolayısıyla bu sürümün bütün RTS şartlarını birebir karşıladığı söylenemez. Bu uyarlamaların hocadan açıkça onay alması gerekecektir.

Unity 6, Photon multiplayer, MCP, LLM, RAG, yapılandırılmış eylemler, insan onayı, güvenlik ve AI değerlendirme gereksinimleri ise teknik tasarımın parçası olarak korunmuştur.

---

## 18. Temel Oynanış Döngüsü

**Konuş → Overall geliştir → Seyircileri ikna et → Bağış topla → Paranı biriktir → Seçim gecesinde stratejik rüşvet harca → Şehirleri kazan → Balkon konuşması yap.**

Political Mayhem'de para, oyuncunun konuşma başarısının seçim gecesinde kullanabileceği son stratejik avantaja dönüşmesini sağlar.