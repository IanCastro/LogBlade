# Contratos de comportamento

Este documento e a referencia canonica para o comportamento implementado de parser e search no LogBlade. Ele descreve somente o que esta presente no codigo atual.

## Terminologia

- **Linha fisica**: linha lida diretamente do arquivo. Ela possui numero e offsets proprios no arquivo original.
- **Registro logico**: unidade entregue pelo parser. Normalmente corresponde a uma linha fisica, mas pode combinar varias linhas fisicas, como na reconstrucao de um JSON dividido.
- **Linha explicita**: linha produzida pelo texto final de um registro logico. Quebras `\r\n`, `\n` e `\r` geradas pelo parser separam linhas explicitas.
- **Segmento visual**: parte de ate 4096 caracteres criada apenas para desenhar uma linha longa no viewer principal. Segmentos visuais nao sao linhas novas e nao alteram a semantica do search.

## Responsabilidades entre back e front

O backend trabalha somente com registros logicos completos. Um source de registros entrega offsets, texto exibido, texto logico completo e, nos resultados de search, cells e headers. Resultados de search tambem preservam o numero da primeira linha fisica no descriptor e na coluna `#`. O source do viewer principal nao calcula numero de linha. Ele nao conhece segmentos de 4096 caracteres, selecao textual ou highlighting.

O front projeta esses registros em rows visuais. No viewer principal, ele separa newlines gerados pelo parser, preserva linhas vazias e divide cada linha explicita em segmentos de ate 4096 caracteres. Nos resultados de search, ele cria uma row por linha explicita aprovada, sem wrap de 4096. Highlighting, selecao, duplo clique e copia usam essa projecao e o texto logico completo fornecido pelo backend.

Navegacao entre registros consulta o source do backend. Navegacao entre segmentos do mesmo registro acontece somente na projecao do front.

## Pipeline do parser

1. O arquivo e lido como linhas fisicas.
2. A cadeia de parser e executada antes de qualquer quebra visual.
3. O parser pode combinar linhas fisicas em um registro logico e pode produzir texto com varias linhas explicitas.
4. O backend entrega o registro logico completo ao front.
5. O viewer principal divide linhas explicitas longas em segmentos visuais de 4096 caracteres somente para exibicao.

Quando uma cadeia possui um estagio JSON, os estagios anteriores podem extrair fragmentos de linhas fisicas consecutivas. Um JSON incompleto e acumulado sem separador ate ficar completo. O limite de um registro combinado e 4096 linhas fisicas ou 16 MiB de texto. Se a continuacao falhar, o JSON for invalido, o limite for excedido ou o arquivo terminar com o JSON incompleto, as linhas fisicas acumuladas sao preservadas como texto original.

Os campos que produzem texto (`Display` de Regex, template JSON e `Replacement` de Regex Replace) interpretam `\n`, `\r`, `\t`, `\\` e escapes Unicode de quatro digitos (`\uFFFF`). `\\n` produz os caracteres literais `\n`, e escapes desconhecidos permanecem literais. Patterns de Regex e Filter nao passam por essa decodificacao.

Todos os patterns Regex do Search, Filter, stage Regex e Regex Replace usam o engine `NonBacktracking` com `CultureInvariant` e sem timeout. Grupos numerados e nomeados sao suportados, inclusive em replacements, mas construcoes incompativeis com `NonBacktracking`, como backreferences no pattern e lookarounds, sao rejeitadas na validacao.

O preview em tempo real so recria readers e reinicia searches quando o pipeline efetivo muda. Publicacoes repetidas da mesma configuracao e alteracoes apenas em nome, amostra ou propriedades ignoradas pelo modo do stage nao recalculam a janela principal.

### Estagios Filter

Um estagio `Filter` avalia cada linha explicita existente naquele ponto da cadeia. Linhas que nao satisfazem o pattern sao removidas; cada linha aprovada continua de forma independente pelos estagios seguintes. `Filter` suporta texto literal ou Regex, `Ignore case` e `Invert match`.

Os estagios de transformacao entre dois Filters pertencem ao nivel do primeiro Filter. Em `A -> Filter 1 -> B -> Filter 2 -> C`, o primeiro nivel mostra a saida de `B` e o segundo mostra a saida de `C`. Se uma transformacao posterior produzir varias linhas explicitas, todas continuam associadas a linha aprovada e repetem as capturas Regex do Filter que originou aquele nivel.

Reconstrucao de JSON entre varias linhas fisicas e permitida somente nos estagios anteriores ao primeiro Filter. Depois dele, um estagio JSON pode processar uma linha explicita que ja contenha um JSON completo, mas nunca acumula linhas fisicas ou linhas aprovadas diferentes.

Quando a regra efetiva possui Filters, o viewer principal fica oculto. Cada Filter aparece como um nivel fixo e bloqueado de search; buscas manuais aparecem abaixo desses niveis. Remover ou trocar para uma regra sem Filter restaura o viewer principal e remove somente os niveis fixos.

## Semantica do search

- O search recebe o texto final do parser, nunca os segmentos visuais de 4096 caracteres.
- Cada linha explicita e avaliada separadamente por search literal ou Regex.
- Somente linhas explicitas que satisfazem o search aparecem nos resultados.
- Se duas linhas explicitas do mesmo registro derem match, elas geram dois resultados distintos.
- Uma linha explicita maior que 4096 caracteres continua sendo um unico resultado de search.
- Uma Regex nao atravessa quebras de linha geradas pelo parser.
- Capturas Regex pertencem somente a linha explicita que deu match.
- `Invert match` tambem e avaliado separadamente para cada linha explicita.

Exemplo: se o parser produzir `ERROR\nfailed`, um search por `failed` retorna somente `failed`. Um search Regex por `ERROR\nfailed` nao retorna resultado.

## Searches em cascata

Cada nivel recebe somente os resultados aprovados pelo nivel anterior. A unidade preservada entre os niveis e a linha explicita, nao o registro logico inteiro.

Filters do parser formam um prefixo fixo da cascata. Searches manuais formam o sufixo editavel e recebem somente as linhas sobreviventes do ultimo Filter. Alterar um search manual inferior nao recalcula nem cancela os niveis fixos ja completos.

No exemplo `ERROR\nfailed`, se o primeiro nivel procurar `ERROR` e o segundo procurar `failed`, o segundo nivel nao encontra resultado: `failed` e uma linha irma do mesmo registro, mas nao passou pelo primeiro nivel.

Alterar um nivel inferior, continuar uma busca, processar append ou retomar um checkpoint deve preservar essa mesma identidade de linha explicita.

## Mapeamento ao arquivo original

- Todo resultado mantem os offsets e o numero da primeira linha fisica do registro logico original.
- Linhas explicitas do mesmo registro podem compartilhar esses offsets.
- `ExplicitRowIndex` distingue qual linha explicita gerou cada resultado.
- Navegacao e sincronizacao usam os offsets originais; selecao e exibicao usam tambem o indice da linha explicita.

## Selecao e copia

- Quebras de linha geradas pelo parser sao preservadas ao copiar o texto exibido.
- Segmentos visuais de 4096 caracteres sao reunidos sem inserir quebras de linha.
- Ao copiar varias linhas ou resultados, cada item selecionado continua separado por uma quebra de linha do clipboard.
- Tabs continuam sendo convertidos em um espaco na copia.
- `Ctrl+Shift+C` inclui os headers correspondentes as colunas copiadas dos resultados de search; no viewer principal, que nao possui headers, equivale a `Ctrl+C`.

## Exportacao com Ctrl+S

- Com search ativo, incluindo uma regra de parser com Filter, `Ctrl+S` salva o ultimo nivel como TSV com cabecalho e todas as colunas (`#`, `Text` e capturas).
- Sem search ativo, `Ctrl+S` salva todos os registros exibidos pelo parser. Newlines do parser sao preservados e segmentos visuais de 4096 caracteres nao criam quebras no arquivo.
- Sem parser, a exportacao usa o texto original decodificado. As saidas sao gravadas em UTF-8 com BOM e registros sao separados por CRLF.

## Manutencao

Qualquer mudanca nesses contratos deve atualizar este documento e os smoke tests correspondentes no mesmo trabalho. Em especial, os testes devem continuar cobrindo saida multiline, linhas maiores que 4096 caracteres, Regex e capturas por linha, `Invert match`, cascata, alteracao de nivel inferior e append.

Funcionalidades existentes somente em stash ou ainda planejadas nao fazem parte deste contrato.
