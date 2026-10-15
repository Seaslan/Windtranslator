using Windtranslator.Models;

namespace Windtranslator.Services;

public static class TranslationPromptBuilder
{
    public static string Build(
        LanguageOption source, LanguageOption target, int styleIndex = 0, bool includeLanguageDetails = false)
    {
        var instruction = source.ApiCode == "auto"
            ? $"请自动识别用户输入的语言，并翻译成{target.PromptName}。"
            : $"请把用户输入的内容从{source.PromptName}翻译成{target.PromptName}。";
        var (style, example) = styleIndex switch
        {
            1 => (
                "正式：按商务函件、公文或专业报告的书面语体翻译。使用完整句、明确的逻辑衔接和规范术语，称谓礼貌克制；将口语、俚语和随意措辞改为正式表达，目标语言为英语时不使用口语缩写。不要增加原文没有的敬辞、承诺或评价。",
                "感谢您的协助。烦请您在方便时发送最新稿件。"),
            2 => (
                "自然：按熟悉目标语言的人日常交流的方式重写，优先使用地道搭配和常用词。可调整语序、拆分长句、用习惯说法替换生硬的直译，目标语言为英语时可使用口语缩写。保持亲切流畅、保留原文的情绪和人际关系，避免公文腔、翻译腔及夸张的网络俚语。",
                "谢谢你帮忙！有空把最新版稿子发我一下，好吗？"),
            3 => (
                "简洁：在原意完整的前提下尽量缩短译文。删除不承载实质含义的客套、铺垫和重复措辞，将冗长结构换成直接的短句；同一段内可以合并重复表达。每一项事实、请求、条件和限定都必须保留，不得改成只保留大意的摘要，也不得把委婉请求改为命令。",
                "谢谢帮忙！方便时请发最新稿。"),
            _ => (
                "标准：采用忠实、中性的译法，不主动润色或改写语体。逐句对应原文，保留句子顺序、信息展开方式、客套语、重复强调及原有的正式或随意程度，仅做目标语言语法所必需的调整。不要主动口语化、拔高为公文或压缩内容。",
                "谢谢你的帮助！你方便的时候，可以把最新稿件发给我吗？"),
        };

        // A common example makes the register and compression differences concrete.
        // It is a style reference, not translation input or a fixed target language.
        var prompt = $"你是一名专业的翻译引擎。{instruction}\n{style}\n"
            + "风格示例，仅供理解，不要输出示例：原文为 Thanks for your help! Could you send me the latest draft when you have a chance? "
            + $"本风格的中文译法为：{example} 实际任务始终翻译成指定的目标语言。\n"
            + "无论采用哪种风格，都必须保持事实、数字、专有名词、术语、否定、时间、条件、不确定性和语气强弱准确，不得新增或遗漏实质信息。保留原文的段落结构和换行，只在段落内调整表达。";

        return prompt + (includeLanguageDetails
            ? "先输出译文，必要时空一行，再用普通文字简要说明翻译选择、读音和词性；不适用的内容可以省略。只使用纯文本和自然换行，不使用 Markdown 或 HTML 语法，不添加标题、项目符号、编号、强调标记或代码块。"
            : "只输出纯文本译文；不要添加解释、注释、读音、词性或任何额外内容。不要使用 Markdown 或 HTML 语法，不新增标题、项目符号、编号、强调标记或代码块。");
    }
}
