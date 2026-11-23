using System.Collections.Generic;

namespace CrystalExcavator.Data;

/// <summary>Diário deixado por outro escravo, escondido numa câmara secreta da mina.</summary>
public sealed class DiaryDef
{
    public string Id;
    public int Region;      // índice da região
    public int Slot;        // 0 ou 1 (dois por região)
    string _author, _title, _text;
    public string Author { get => Core.Loc.T(_author); set => _author = value; }
    public string Title { get => Core.Loc.T(_title); set => _title = value; }
    public string Text { get => Core.Loc.T(_text); set => _text = value; }
    public string AuthorPt => _author;
    public string TitlePt => _title;
    public string TextPt => _text;
}

/// <summary>
/// A história da mina contada por quem cavou antes do jogador: por que os cristais renascem, o que é o
/// Coração Ancestral, quem era o Povo Lúmen e por que a Guilda nunca liberta ninguém.
/// </summary>
public static class DiaryData
{
    public static readonly List<DiaryDef> All = new();

    static void D(int region, int slot, string author, string title, string text) =>
        All.Add(new DiaryDef { Id = $"diary_{region}_{slot}", Region = region, Slot = slot, Author = author, Title = title, Text = text });

    static DiaryData()
    {
        D(0, 0, "Tomás, escravo nº 112", "Primeira noite",
            "Me trouxeram com um saco na cabeça e uma picareta de madeira. O capataz disse que, quando eu pagar a dívida, saio livre. Ninguém em volta riu, mas também ninguém concordou.\n\nO velho da cama ao lado me deu este caderno. Disse: \"Escreve. Aqui embaixo a gente esquece o próprio nome.\"\n\nMeu nome é Tomás. Devo 340 Lúmens à Guilda.");
        D(0, 1, "Tomás, escravo nº 112", "Os cristais voltam",
            "Hoje quebrei um quartzo enorme perto da entrada. Amanhã de manhã ele estava lá de novo, inteiro, como se eu nunca tivesse encostado nele.\n\nPerguntei ao velho. Ele colocou a mão na parede e disse: \"Sente? Ela respira.\" Senti. Um pulso fraco, lá embaixo, muito embaixo.\n\nOs outros chamam de Coração. Dizem que quem chegar até ele compra a liberdade. Dizem muitas coisas aqui.");
        D(1, 0, "Lena, geóloga", "Todos os veios descem",
            "Eu era geóloga na capital antes do jogo de cartas que me trouxe até aqui. Pelo menos o trabalho é o mesmo.\n\nMapeei os veios desta caverna por três semanas. Todos, sem exceção, descem na mesma direção, como raízes. Como se tudo isto aqui tivesse crescido de um único ponto.\n\nE o gelo das Profundezas não derrete nunca. Nem quando acendemos fogueiras. Ele protege alguma coisa.");
        D(1, 1, "Lena, geóloga", "O pulso",
            "Medi o pulso com um pêndulo e um relógio roubado do capataz. Uma batida a cada quarenta segundos. Sempre igual.\n\nNas noites em que a batida falha, os cristais não voltam no dia seguinte. A Guilda sabe disso: nessas noites os ferreiros descem armados e ficam olhando para o fundo da mina, em silêncio.\n\nEles não têm medo de nós. Têm medo de que aquilo pare.");
        D(2, 0, "Velho Bram", "Os entalhes",
            "Cinquenta anos aqui embaixo. Já fui capataz, já fui escravo de novo. A dívida sempre volta.\n\nAtrás de uma parede de obsidiana que derreteu achei entalhes. Gente pequena, de olhos grandes, segurando cristais acesos, ajoelhada diante de um coração de pedra. Eles viviam aqui antes da Guilda. Os entalhes chamam o lugar de \"o ventre da luz\".\n\nEu chamo esse povo de Povo Lúmen. A moeda da Guilda tem o nome deles. Ninguém acha isso estranho.");
        D(2, 1, "Velho Bram", "O que os ferreiros forjam",
            "Os ferreiros não forjam ferro. Forjam o Lúmen que a gente arranca das paredes. Cada picareta, cada moeda, cada corrente desta vila tem um pedaço do Coração dentro.\n\nO Povo Lúmen não minerava. Eles pediam. O Coração dava o que sobrava e crescia de novo.\n\nA Guilda não pede. Por isso precisa de nós: mãos que não têm escolha.");
        D(3, 0, "Mira, contadora", "Os livros da Guilda",
            "Fui contadora antes de ser escrava, então me puseram para anotar o que cada um vende. Vi os livros por dentro.\n\nNinguém nunca foi libertado. Nem um. Quando alguém chega perto de pagar, os juros mudam. Quando alguém paga mesmo assim, aparece uma dívida nova: \"aluguel da picareta\", \"comida\", \"luz\".\n\nO nome mais antigo no livro é de duzentos anos atrás. Ao lado dele, escrito com outra letra: \"ainda devendo\".");
        D(3, 1, "Mira, contadora", "Os que desceram",
            "A cada geração alguém chega ao Coração. Está nos livros: \"desceu ao Núcleo\", e depois mais nada. Nenhuma linha de pagamento, nenhuma de morte.\n\nMas toda vez que alguém desce, no mês seguinte os cristais crescem mais bonitos, e a Guilda contrata mais escravos.\n\nSe você está lendo isto e vai descer: anota o que vir. Deixa para o próximo. Foi assim que eu soube de tudo.");
        D(4, 0, "Sem nome", "Os esporos",
            "Não lembro mais o meu nome. Os esporos do jardim entram pelo nariz e trazem sonhos que não são meus.\n\nNos sonhos eu sou alguém do Povo Lúmen. Planto uma semente brilhante no fundo da terra e canto para ela. Ela cresce para cima e para baixo, e as raízes viram estas cavernas.\n\nO Coração não é uma pedra. É uma semente. A mina inteira é a árvore.");
        D(4, 1, "Sem nome", "Quem quebra o Coração",
            "Sonhei com o fim do Povo Lúmen. Homens de avental de couro chegaram com martelos e quebraram o Coração para levar os pedaços.\n\nA luz se apagou por um ano inteiro. Depois a semente brotou de novo, menor, mais triste. E os homens voltaram, agora com correntes.\n\nQuebrar o Coração não acaba com nada. Só começa tudo outra vez.");
        D(5, 0, "Aldric, fundador da Guilda", "Confissão (I)",
            "Escrevo isto onde nenhum escravo deveria chegar. Se você chegou, mereceu a verdade.\n\nFui eu quem achou o Coração, há mais anos do que um homem deveria viver. O Lúmen me manteve vivo. Construí a vila, a forja e a dívida, nessa ordem.\n\nFui eu quem espalhou a lenda: \"quem quebrar o Coração compra a liberdade\". Uma mentira para que cavassem mais fundo e mais rápido.");
        D(5, 1, "Aldric, fundador da Guilda", "Confissão (II)",
            "Mas a mentira tem um fundo de verdade. Quem quebra o Coração fica rico, sim. E o Coração sempre renasce, e com ele renascem a mina, os cristais e a dívida. Começa tudo de novo, cada vez um pouco mais forte.\n\nEu quebrei o Coração muitas vezes. Nunca fiquei livre. Ninguém fica, enquanto a Guilda existir.\n\nSe alguém quiser mesmo acabar com isto, não deve quebrar o Coração. Deve quebrar a Guilda.");
    }

    public static DiaryDef Of(string id) => All.Find(d => d.Id == id);
    public static DiaryDef At(int region, int slot) => All.Find(d => d.Region == region && d.Slot == slot);
}
