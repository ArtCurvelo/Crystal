using System.Collections.Generic;

namespace CrystalExcavator.Data;

/// <summary>Uma fala de diálogo: quem fala ("bram", "hero") e o texto em português (traduzido na exibição).</summary>
public readonly record struct Line(string Speaker, string Text);

/// <summary>
/// Ato 2 — "A Chave Falsa" (docs/STORY.md). Etapas guardadas em SaveData.StoryStage:
/// 0 antes do Coração · 1 Bram chama · 2 achar o portão e tirar o molde · 3 levar o molde ao Bram ·
/// 4 juntar minérios, ler o Aldric e forjar a chave (à noite) · 5 abrir o portão · 6 portão aberto.
/// </summary>
public static class StoryData
{
    public const int Locked = 0, BramCalls = 1, FindGate = 2, BringMold = 3, ForgeKey = 4, OpenGate = 5, GateOpen = 6;
    public const string KeyOre1 = "meteorite", KeyOre2 = "diamond";
    public const int KeyOre1Amount = 30, KeyOre2Amount = 12;
    public static readonly long KeyFee = 250_000;
    public static readonly string[] AldricDiaries = { "diary_5_0", "diary_5_1" };

    public static readonly string[] Grumbles =
    {
        "Some daqui, escravinho. Volta quando tiver feito alguma coisa que preste.",
        "Tá olhando o quê? Nunca viu um velho sem perna? Vai cavar.",
        "Os ferreiros sorriem pra você, né? Sorriam pra mim também. Vai, vai trabalhar.",
        "Dizem que quem quebra o Coração fica livre. Quebra e depois a gente conversa.",
    };

    public static readonly Line[] Intro1 =
    {
        new("bram", "Ei, você. Quebrou o Coração, foi? Eu ouvi a mina gemer lá de baixo."),
        new("hero", "Quem é você?"),
        new("bram", "Bram. Já fui escravo, já fui capataz, já fui escravo de novo. Se você leu uns cadernos lá embaixo, já me conhece."),
        new("hero", "Os entalhes do Povo Lúmen... foi você que escreveu!"),
        new("bram", "Fui. E agora vou te contar o que eu não tive coragem de escrever. A coisa que a Guilda quer que todo mundo esqueça."),
    };

    public static readonly string[] Flashback =
    {
        "Corvo era meu dono. Um ferreiro dos grandes. Um dia ele achou um portão no fundo da Catedral, mais antigo que a própria Guilda. E me mandou entrar primeiro.",
        "Lá dentro o Lúmen não é pedra. Ele anda. Ele tem dentes. Eu deixei lá um olho e uma perna.",
        "Quando me arrastaram de volta, a vila inteira viu. A Guilda não podia deixar a verdade escapar: expulsaram o Corvo para salvar o próprio nome.",
        "Lacraram o portão com o selo do Mestre. Mas antes do lacre o Corvo voltou lá, sozinho. Desceu. E nunca mais subiu.",
    };

    public static readonly Line[] Intro2 =
    {
        new("bram", "Atrás daquele portão está a raiz de tudo. É de lá que vem o poder deles. E é lá que o Corvo apodrece até hoje."),
        new("hero", "E o que eu ganho entrando lá?"),
        new("bram", "A única coisa que vale alguma coisa aqui: uma chance de acabar com a Guilda. O Coração renasce. A Guilda, não."),
        new("bram", "Toma esta cera. Desce até o fundo da Catedral de Cristal, acha o portão e tira um molde do selo."),
    };

    public static readonly Line[] NeedCathedral =
    {
        new("bram", "Ainda não abriu caminho até a Catedral? Cava, junta Lúmen, paga o que eles pedem. Eu espero. Esperei a vida inteira."),
    };

    public static readonly Line[] RemindGate =
    {
        new("bram", "O portão fica no fundo da Catedral de Cristal, no ponto mais fundo. Encosta a cera no selo e volta aqui."),
    };

    public static readonly Line[] Mold =
    {
        new("bram", "Deixa eu ver esse molde... É o selo do Mestre, sim. Dá pra sentir a raiva dele na cera."),
        new("bram", "Uma chave dessas precisa de metal que caiu do céu e de diamante da Catedral: trinta Ferros Meteóricos e doze Diamantes."),
        new("bram", "E tem mais. A Guilda grava um código em cada chave. O Aldric escrevia tudo... os cadernos dele estão escondidos na Catedral. Lê os dois."),
        new("hero", "E onde eu forjo isso sem o ferreiro ver?"),
        new("bram", "Na forja dele, de noite, quando ele estiver dormindo. Deixei a receita escondida embaixo da bigorna. Não conta pra ninguém."),
    };

    public static readonly Line[] RemindKey =
    {
        new("bram", "Trinta Ferros Meteóricos, doze Diamantes, os dois cadernos do Aldric. Depois, a forja, de noite. Anda logo, que eu não fico mais novo."),
    };

    public static readonly Line[] KeyReady =
    {
        new("bram", "Essa chave enganaria o próprio Aldric. Vai até o portão."),
        new("bram", "E se encontrar o Corvo lá embaixo... diz que o Bram mandou lembranças."),
    };

    public static readonly Line[] AfterGate =
    {
        new("bram", "Você abriu? ... Então começou. Lá embaixo são seis lugares, um pior que o outro. O Corvo está no último."),
        new("bram", "E escuta: lá embaixo picareta não basta. Deixei na forja a receita das armas, espada e martelo. Forja uma antes de descer."),
        new("bram", "Prepara as ferramentas, garoto. E que o Povo Lúmen te guarde."),
    };

    public static string SpeakerName(string id) => Core.Loc.T(id == "bram" ? "Velho Bram" : "Você");
}
