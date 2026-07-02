# Contratos de comportamento

Este documento e a referencia canonica para o comportamento implementado de parser e search no LogBlade. Ele descreve somente o que esta presente no codigo atual.

## Terminologia

- **Linha fisica**: linha lida diretamente do arquivo. Ela possui numero e offsets proprios no arquivo original.
- **Registro logico**: unidade entregue pelo parser. Normalmente corresponde a uma linha fisica, mas pode combinar varias linhas fisicas, como na reconstrucao de um JSON dividido.
- **Linha explicita**: linha produzida pelo texto final de um registro logico. Quebras `\r\n`, `\n` e `\r` geradas pelo parser separam linhas explicitas.
- **Segmento visual**: parte de ate 4096 caracteres criada apenas para desenhar uma linha longa no viewer principal. Segmentos visuais nao sao linhas novas e nao alteram a semantica do search.

## Pipeline do parser

1. O arquivo e lido como linhas fisicas.
2. A cadeia de parser e executada antes de qualquer quebra visual.
3. O parser pode combinar linhas fisicas em um registro logico e pode produzir texto com varias linhas explicitas.
4. O viewer principal divide linhas explicitas longas em segmentos visuais de 4096 caracteres somente para exibicao.

Quando uma cadeia possui um estagio JSON, os estagios anteriores podem extrair fragmentos de linhas fisicas consecutivas. Um JSON incompleto e acumulado sem separador ate ficar completo. O limite de um registro combinado e 4096 linhas fisicas ou 16 MiB de texto. Se a continuacao falhar, o JSON for invalido, o limite for excedido ou o arquivo terminar com o JSON incompleto, as linhas fisicas acumuladas sao preservadas como texto original.

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

## Manutencao

Qualquer mudanca nesses contratos deve atualizar este documento e os smoke tests correspondentes no mesmo trabalho. Em especial, os testes devem continuar cobrindo saida multiline, linhas maiores que 4096 caracteres, Regex e capturas por linha, `Invert match`, cascata, alteracao de nivel inferior e append.

Funcionalidades existentes somente em stash ou ainda planejadas nao fazem parte deste contrato.
