using System.Globalization;

namespace LaptopQaUsbBuilder;

public static class Localization
{
    private static readonly string[] Keys = ["Subtitle", "Select USB Drive", "Refresh", "Select All", "Clear All", "Partition Layout", "GPT Note", "Merge Hint", "Add Folder", "Remove", "XML Optional", "Select XML", "Build Summary", "Targets", "Partition style", "Data sources", "Warning", "Activity", "Confirm ERASE", "Build USB Queue", "Ready", "Partition Settings", "Config Subtitle", "Language", "Theme", "Partition count", "Remaining Hint", "Volume label", "Size Header", "Format", "Size Help", "Restore Defaults", "Cancel", "Save", "Light", "Dark"];

    public static readonly IReadOnlyList<LanguageOption> Languages =
    [
        new("en-US", "English"), new("es-ES", "Español"), new("fr-FR", "Français"), new("de-DE", "Deutsch"),
        new("pt-BR", "Português"), new("zh-CN", "简体中文"), new("ja-JP", "日本語"), new("hi-IN", "हिन्दी"),
        new("bn-IN", "বাংলা"), new("ta-IN", "தமிழ்"), new("te-IN", "తెలుగు"), new("mr-IN", "मराठी")
    ];

    private static readonly Dictionary<string, Dictionary<string, string>> Packs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["es-ES"] = Pack("Preparar medios de soporte estandarizados de forma segura y uniforme", "Seleccionar unidades USB", "Actualizar", "Seleccionar todo", "Borrar selección", "Diseño de particiones", "Cada disco seleccionado usará una tabla de particiones GPT.", "El contenido de las carpetas se combina en la raíz de la partición.", "Añadir carpeta", "Quitar", "Autounattend.xml (opcional)", "Seleccionar XML", "Resumen de creación", "Destinos", "Estilo de partición", "Fuentes de datos", "ADVERTENCIA: Todas las particiones y archivos de las unidades USB seleccionadas se borrarán permanentemente.", "Actividad", "Escriba ERASE para confirmar", "Crear cola USB", "Listo", "Configuración de particiones", "Configure el diseño GPT utilizado en futuras creaciones.", "Idioma", "Tema", "Cantidad de particiones", "La partición final siempre utiliza el espacio restante del disco.", "Etiqueta del volumen", "Tamaño (MB, GB o Remaining)", "Formato", "Ejemplos: 50 MB, 20 GB. Use Remaining solo para la partición final.", "Restaurar valores", "Cancelar", "Guardar", "Claro", "Oscuro"),
        ["fr-FR"] = Pack("Préparer des supports normalisés de manière sûre et cohérente", "Sélectionner les clés USB", "Actualiser", "Tout sélectionner", "Tout effacer", "Disposition des partitions", "Chaque disque sélectionné utilisera une table de partitions GPT.", "Le contenu des dossiers est fusionné à la racine de la partition.", "Ajouter un dossier", "Supprimer", "Autounattend.xml (facultatif)", "Sélectionner XML", "Résumé de création", "Cibles", "Style de partition", "Sources de données", "AVERTISSEMENT : toutes les partitions et tous les fichiers des clés USB sélectionnées seront définitivement effacés.", "Activité", "Saisissez ERASE pour confirmer", "Créer la file USB", "Prêt", "Paramètres des partitions", "Configurez la disposition GPT utilisée pour les prochaines créations.", "Langue", "Thème", "Nombre de partitions", "La dernière partition utilise toujours l’espace disque restant.", "Nom du volume", "Taille (MB, GB ou Remaining)", "Format", "Exemples : 50 MB, 20 GB. Utilisez Remaining uniquement pour la dernière partition.", "Valeurs par défaut", "Annuler", "Enregistrer", "Clair", "Sombre"),
        ["de-DE"] = Pack("Standardisierte Supportmedien sicher und einheitlich vorbereiten", "USB-Laufwerke auswählen", "Aktualisieren", "Alle auswählen", "Auswahl löschen", "Partitionslayout", "Jeder ausgewählte Datenträger verwendet eine GPT-Partitionstabelle.", "Ordnerinhalte werden im Stamm der Partition zusammengeführt.", "Ordner hinzufügen", "Entfernen", "Autounattend.xml (optional)", "XML auswählen", "Build-Zusammenfassung", "Ziele", "Partitionsstil", "Datenquellen", "WARNUNG: Alle Partitionen und Dateien auf den ausgewählten USB-Laufwerken werden dauerhaft gelöscht.", "Aktivität", "Zur Bestätigung ERASE eingeben", "USB-Warteschlange erstellen", "Bereit", "Partitionseinstellungen", "Konfigurieren Sie das GPT-Layout für zukünftige Builds.", "Sprache", "Design", "Partitionsanzahl", "Die letzte Partition verwendet immer den verbleibenden Speicherplatz.", "Volumebezeichnung", "Größe (MB, GB oder Remaining)", "Format", "Beispiele: 50 MB, 20 GB. Remaining nur für die letzte Partition verwenden.", "Standardwerte", "Abbrechen", "Speichern", "Hell", "Dunkel"),
        ["pt-BR"] = Pack("Prepare mídias de suporte padronizadas com segurança e consistência", "Selecionar unidades USB", "Atualizar", "Selecionar tudo", "Limpar seleção", "Layout de partições", "Cada disco selecionado usará uma tabela de partições GPT.", "O conteúdo das pastas é mesclado na raiz da partição.", "Adicionar pasta", "Remover", "Autounattend.xml (opcional)", "Selecionar XML", "Resumo da criação", "Destinos", "Estilo de partição", "Fontes de dados", "AVISO: Todas as partições e arquivos nas unidades USB selecionadas serão apagados permanentemente.", "Atividade", "Digite ERASE para confirmar", "Criar fila USB", "Pronto", "Configurações de partição", "Configure o layout GPT usado em futuras criações.", "Idioma", "Tema", "Quantidade de partições", "A partição final sempre usa o espaço restante do disco.", "Rótulo do volume", "Tamanho (MB, GB ou Remaining)", "Formato", "Exemplos: 50 MB, 20 GB. Use Remaining apenas na partição final.", "Restaurar padrões", "Cancelar", "Salvar", "Claro", "Escuro"),
        ["zh-CN"] = Pack("安全、一致地准备标准化支持介质", "选择 USB 驱动器", "刷新", "全选", "清除全部", "分区布局", "每个选定磁盘都将使用 GPT 分区表。", "文件夹内容将合并到分区根目录。", "添加文件夹", "移除", "Autounattend.xml（可选）", "选择 XML", "构建摘要", "目标", "分区样式", "数据源", "警告：所选 USB 驱动器上的所有分区和文件都将被永久删除。", "活动", "输入 ERASE 以确认", "构建 USB 队列", "就绪", "分区设置", "配置用于后续构建的 GPT 布局。", "语言", "主题", "分区数量", "最后一个分区始终使用磁盘剩余空间。", "卷标", "大小（MB、GB 或 Remaining）", "格式", "示例：50 MB、20 GB。仅最后一个分区使用 Remaining。", "恢复默认值", "取消", "保存", "浅色", "深色"),
        ["ja-JP"] = Pack("標準化されたサポートメディアを安全かつ一貫して準備します", "USB ドライブを選択", "更新", "すべて選択", "選択を解除", "パーティション構成", "選択した各ディスクは GPT パーティションテーブルを使用します。", "フォルダーの内容はパーティションのルートに統合されます。", "フォルダーを追加", "削除", "Autounattend.xml（任意）", "XML を選択", "ビルド概要", "対象", "パーティション形式", "データソース", "警告：選択した USB ドライブ上のすべてのパーティションとファイルは完全に消去されます。", "アクティビティ", "確認のため ERASE と入力", "USB キューを作成", "準備完了", "パーティション設定", "今後のビルドで使用する GPT 構成を設定します。", "言語", "テーマ", "パーティション数", "最後のパーティションは常に残りのディスク領域を使用します。", "ボリュームラベル", "サイズ（MB、GB、Remaining）", "形式", "例：50 MB、20 GB。Remaining は最後のパーティションにのみ使用します。", "既定値に戻す", "キャンセル", "保存", "ライト", "ダーク"),
        ["hi-IN"] = Pack("मानकीकृत सहायता मीडिया सुरक्षित और सुसंगत रूप से तैयार करें", "USB ड्राइव चुनें", "ताज़ा करें", "सभी चुनें", "सभी हटाएँ", "पार्टिशन लेआउट", "प्रत्येक चयनित डिस्क GPT पार्टिशन तालिका का उपयोग करेगी।", "फ़ोल्डर की सामग्री पार्टिशन के मूल में जोड़ी जाती है।", "फ़ोल्डर जोड़ें", "हटाएँ", "Autounattend.xml (वैकल्पिक)", "XML चुनें", "बिल्ड सारांश", "लक्ष्य", "पार्टिशन शैली", "डेटा स्रोत", "चेतावनी: चयनित USB ड्राइव के सभी पार्टिशन और फ़ाइलें स्थायी रूप से मिटा दी जाएँगी।", "गतिविधि", "पुष्टि के लिए ERASE लिखें", "USB कतार बनाएँ", "तैयार", "पार्टिशन सेटिंग्स", "भविष्य के बिल्ड के लिए GPT लेआउट कॉन्फ़िगर करें।", "भाषा", "थीम", "पार्टिशन संख्या", "अंतिम पार्टिशन हमेशा शेष डिस्क स्थान का उपयोग करता है।", "वॉल्यूम लेबल", "आकार (MB, GB या Remaining)", "फ़ॉर्मेट", "उदाहरण: 50 MB, 20 GB। Remaining केवल अंतिम पार्टिशन के लिए उपयोग करें।", "डिफ़ॉल्ट पुनर्स्थापित करें", "रद्द करें", "सहेजें", "हल्का", "गहरा"),
        ["bn-IN"] = Pack("মানসম্মত সহায়তা মিডিয়া নিরাপদে ও ধারাবাহিকভাবে প্রস্তুত করুন", "USB ড্রাইভ নির্বাচন করুন", "রিফ্রেশ", "সব নির্বাচন", "সব মুছুন", "পার্টিশন বিন্যাস", "প্রতিটি নির্বাচিত ডিস্ক GPT পার্টিশন টেবিল ব্যবহার করবে।", "ফোল্ডারের বিষয়বস্তু পার্টিশনের রুটে একত্রিত হয়।", "ফোল্ডার যোগ করুন", "সরান", "Autounattend.xml (ঐচ্ছিক)", "XML নির্বাচন", "বিল্ড সারাংশ", "লক্ষ্য", "পার্টিশন স্টাইল", "ডেটা উৎস", "সতর্কতা: নির্বাচিত USB ড্রাইভের সব পার্টিশন ও ফাইল স্থায়ীভাবে মুছে যাবে।", "কার্যকলাপ", "নিশ্চিত করতে ERASE লিখুন", "USB সারি তৈরি করুন", "প্রস্তুত", "পার্টিশন সেটিংস", "ভবিষ্যৎ বিল্ডের GPT বিন্যাস কনফিগার করুন।", "ভাষা", "থিম", "পার্টিশন সংখ্যা", "শেষ পার্টিশন সবসময় অবশিষ্ট ডিস্ক স্থান ব্যবহার করে।", "ভলিউম লেবেল", "আকার (MB, GB বা Remaining)", "ফরম্যাট", "উদাহরণ: 50 MB, 20 GB। Remaining শুধু শেষ পার্টিশনে ব্যবহার করুন।", "ডিফল্ট পুনরুদ্ধার", "বাতিল", "সংরক্ষণ", "হালকা", "গাঢ়"),
        ["ta-IN"] = Pack("தரப்படுத்தப்பட்ட ஆதரவு ஊடகத்தை பாதுகாப்பாகவும் சீராகவும் தயாரிக்கவும்", "USB இயக்கிகளைத் தேர்ந்தெடுக்கவும்", "புதுப்பிக்கவும்", "அனைத்தையும் தேர்ந்தெடு", "அனைத்தையும் நீக்கு", "பகிர்வு அமைப்பு", "தேர்ந்தெடுக்கப்பட்ட ஒவ்வொரு வட்டும் GPT பகிர்வு அட்டவணையைப் பயன்படுத்தும்.", "கோப்புறை உள்ளடக்கம் பகிர்வின் மூலத்தில் ஒன்றிணைக்கப்படும்.", "கோப்புறை சேர்", "நீக்கு", "Autounattend.xml (விருப்பம்)", "XML தேர்வு", "உருவாக்கச் சுருக்கம்", "இலக்குகள்", "பகிர்வு பாணி", "தரவு மூலங்கள்", "எச்சரிக்கை: தேர்ந்தெடுக்கப்பட்ட USB இயக்கிகளில் உள்ள அனைத்தும் நிரந்தரமாக அழிக்கப்படும்.", "செயல்பாடு", "உறுதிப்படுத்த ERASE என உள்ளிடவும்", "USB வரிசையை உருவாக்கு", "தயார்", "பகிர்வு அமைப்புகள்", "எதிர்கால உருவாக்கங்களுக்கான GPT அமைப்பை உள்ளமைக்கவும்.", "மொழி", "தீம்", "பகிர்வு எண்ணிக்கை", "இறுதி பகிர்வு மீதமுள்ள இடத்தைப் பயன்படுத்தும்.", "தொகுதி பெயர்", "அளவு (MB, GB அல்லது Remaining)", "வடிவம்", "எடுத்துக்காட்டுகள்: 50 MB, 20 GB. Remaining இறுதி பகிர்வுக்கு மட்டும்.", "இயல்புநிலைகள்", "ரத்து", "சேமி", "ஒளி", "இருள்"),
        ["te-IN"] = Pack("ప్రామాణిక మద్దతు మీడియాను సురక్షితంగా మరియు స్థిరంగా సిద్ధం చేయండి", "USB డ్రైవ్‌లను ఎంచుకోండి", "రిఫ్రెష్", "అన్నీ ఎంచుకోండి", "అన్నీ తొలగించండి", "పార్టిషన్ లేఅవుట్", "ఎంచుకున్న ప్రతి డిస్క్ GPT పార్టిషన్ పట్టికను ఉపయోగిస్తుంది.", "ఫోల్డర్ కంటెంట్ పార్టిషన్ రూట్‌లో విలీనం అవుతుంది.", "ఫోల్డర్ జోడించండి", "తొలగించండి", "Autounattend.xml (ఐచ్ఛికం)", "XML ఎంచుకోండి", "బిల్డ్ సారాంశం", "లక్ష్యాలు", "పార్టిషన్ శైలి", "డేటా మూలాలు", "హెచ్చరిక: ఎంచుకున్న USB డ్రైవ్‌లలోని అన్ని పార్టిషన్‌లు మరియు ఫైల్‌లు శాశ్వతంగా తొలగించబడతాయి.", "కార్యాచరణ", "నిర్ధారించడానికి ERASE టైప్ చేయండి", "USB క్యూ నిర్మించండి", "సిద్ధంగా", "పార్టిషన్ సెట్టింగ్‌లు", "భవిష్యత్ బిల్డ్‌ల GPT లేఅవుట్‌ను కాన్ఫిగర్ చేయండి.", "భాష", "థీమ్", "పార్టిషన్ సంఖ్య", "చివరి పార్టిషన్ మిగిలిన డిస్క్ స్థలాన్ని ఉపయోగిస్తుంది.", "వాల్యూమ్ లేబుల్", "పరిమాణం (MB, GB లేదా Remaining)", "ఫార్మాట్", "ఉదాహరణలు: 50 MB, 20 GB. Remaining చివరి పార్టిషన్‌కు మాత్రమే.", "డిఫాల్ట్‌లను పునరుద్ధరించు", "రద్దు", "సేవ్", "లైట్", "డార్క్"),
        ["mr-IN"] = Pack("प्रमाणित सहाय्य माध्यम सुरक्षितपणे आणि सातत्याने तयार करा", "USB ड्राइव्ह निवडा", "रिफ्रेश", "सर्व निवडा", "सर्व साफ करा", "विभाजन मांडणी", "प्रत्येक निवडलेली डिस्क GPT विभाजन तक्ता वापरेल.", "फोल्डरमधील सामग्री विभाजनाच्या मुळाशी एकत्र केली जाते.", "फोल्डर जोडा", "काढा", "Autounattend.xml (पर्यायी)", "XML निवडा", "बिल्ड सारांश", "लक्ष्ये", "विभाजन शैली", "डेटा स्रोत", "चेतावणी: निवडलेल्या USB ड्राइव्हवरील सर्व विभाजने आणि फायली कायमच्या मिटवल्या जातील.", "क्रियाकलाप", "पुष्टी करण्यासाठी ERASE टाइप करा", "USB रांग तयार करा", "तयार", "विभाजन सेटिंग्ज", "भविष्यातील बिल्डसाठी GPT मांडणी कॉन्फिगर करा.", "भाषा", "थीम", "विभाजन संख्या", "अंतिम विभाजन उर्वरित डिस्क जागा वापरते.", "व्हॉल्यूम लेबल", "आकार (MB, GB किंवा Remaining)", "फॉरमॅट", "उदाहरणे: 50 MB, 20 GB. Remaining फक्त अंतिम विभाजनासाठी.", "डीफॉल्ट पुनर्संचयित करा", "रद्द करा", "जतन करा", "फिकट", "गडद")
    };

    private static Dictionary<string, string> Pack(params string[] values) => Keys.Zip(values).ToDictionary(x => x.First, x => x.Second);

    public static void ValidateAll()
    {
        foreach (var language in Languages)
            foreach (var key in Keys)
                _ = Text(language.Code, key);
    }

    public static string Text(string? language, string key)
    {
        if (key == "Remaining Hint") return RemainingHint(language);
        if (key == "Size Help") return SizeHelp(language);
        var value = Packs.TryGetValue(language ?? "en-US", out var pack) && pack.TryGetValue(key, out var translated) ? translated : English(key);
        return key is "Size Header" or "Size Help"
            ? value.Replace("Remaining", "*", StringComparison.Ordinal)
            : value;
    }

    private static string RemainingHint(string? language) => Resolve(language).Code switch
    {
        "es-ES" => "Use * en una sola partición, en cualquier posición, para utilizar todo el espacio restante.",
        "fr-FR" => "Utilisez * pour une seule partition, à n’importe quelle position, afin d’utiliser tout l’espace restant.",
        "de-DE" => "Verwenden Sie * für genau eine Partition an einer beliebigen Position, um den gesamten Restspeicher zu nutzen.",
        "pt-BR" => "Use * em uma única partição, em qualquer posição, para utilizar todo o espaço restante.",
        "zh-CN" => "在任意位置的一个分区中输入 *，以使用所有剩余空间。",
        "ja-JP" => "任意の位置にある1つのパーティションに * を入力すると、残りの領域をすべて使用します。",
        "hi-IN" => "सभी शेष स्थान का उपयोग करने के लिए किसी भी स्थान पर केवल एक पार्टीशन में * दर्ज करें।",
        "bn-IN" => "সমস্ত অবশিষ্ট স্থান ব্যবহার করতে যেকোনো অবস্থানের একটি পার্টিশনে * লিখুন।",
        "ta-IN" => "மீதமுள்ள இடம் முழுவதையும் பயன்படுத்த எந்த இடத்திலும் ஒரே ஒரு பகிர்வில் * ஐ உள்ளிடவும்.",
        "te-IN" => "మిగిలిన స్థలమంతా ఉపయోగించడానికి ఏ స్థానంలోనైనా ఒకే పార్టిషన్‌లో * నమోదు చేయండి.",
        "mr-IN" => "सर्व उर्वरित जागा वापरण्यासाठी कोणत्याही स्थानावरील फक्त एका विभाजनात * प्रविष्ट करा.",
        _ => "Enter * for any one partition to use all remaining disk space."
    };

    private static string SizeHelp(string? language) => Resolve(language).Code switch
    {
        "es-ES" => "Tamaños: 50 MB, 20 GB o * para el espacio restante. Use * en exactamente una partición, en cualquier posición.",
        "fr-FR" => "Tailles : 50 MB, 20 GB ou * pour l’espace restant. Utilisez * pour une seule partition, à n’importe quelle position.",
        "de-DE" => "Größen: 50 MB, 20 GB oder * für den Restspeicher. Verwenden Sie * für genau eine Partition an beliebiger Position.",
        "pt-BR" => "Tamanhos: 50 MB, 20 GB ou * para o espaço restante. Use * em exatamente uma partição, em qualquer posição.",
        "zh-CN" => "大小：50 MB、20 GB，或用 * 表示剩余空间。* 可用于任意位置，但只能用于一个分区。",
        "ja-JP" => "サイズ: 50 MB、20 GB、または残りの領域を表す *。* は任意の位置の1つのパーティションだけに使用します。",
        "hi-IN" => "आकार: 50 MB, 20 GB, या शेष स्थान के लिए *। * का उपयोग किसी भी स्थान पर केवल एक पार्टीशन में करें।",
        "bn-IN" => "আকার: 50 MB, 20 GB, অথবা অবশিষ্ট স্থানের জন্য *। যেকোনো অবস্থানের শুধু একটি পার্টিশনে * ব্যবহার করুন।",
        "ta-IN" => "அளவுகள்: 50 MB, 20 GB அல்லது மீதமுள்ள இடத்திற்கு *। எந்த இடத்திலும் ஒரே ஒரு பகிர்வில் மட்டும் * ஐ பயன்படுத்தவும்.",
        "te-IN" => "పరిమాణాలు: 50 MB, 20 GB లేదా మిగిలిన స్థలానికి *। ఏ స్థానంలోనైనా ఒకే పార్టిషన్‌లో మాత్రమే * ఉపయోగించండి.",
        "mr-IN" => "आकार: 50 MB, 20 GB किंवा उर्वरित जागेसाठी *। कोणत्याही स्थानावरील फक्त एका विभाजनात * वापरा.",
        _ => "Size entries: 50 MB, 20 GB, or * for all remaining space. Use * for exactly one partition in any position."
    };
    private static string English(string key) => key switch
    {
        "Subtitle" => "Prepare standardized support media safely and consistently", "GPT Note" => "Each selected disk will use a GPT partition table.",
        "Merge Hint" => "Folders merge into the partition root.", "XML Optional" => "Autounattend.xml (optional)",
        "Warning" => "WARNING: Every partition and file on the selected USB drives will be permanently erased.", "Confirm ERASE" => "Type ERASE to confirm",
        "Config Subtitle" => "Configure the GPT layout used for future builds.", "Remaining Hint" => "Enter * for any one partition to use all remaining disk space.",
        "Size Header" => "Size (MB, GB, or *)", "Size Help" => "Size entries: 50 MB, 20 GB, or * for all remaining space. Use * for exactly one partition in any position.",
        _ => key
    };

    public static LanguageOption Resolve(string? code) => Languages.FirstOrDefault(l => l.Code.Equals(code, StringComparison.OrdinalIgnoreCase)) ?? Languages[0];
    public static void ApplyCulture(string? code)
    {
        var culture = CultureInfo.GetCultureInfo(Resolve(code).Code);
        CultureInfo.DefaultThreadCurrentCulture = culture; CultureInfo.DefaultThreadCurrentUICulture = culture;
    }
}
