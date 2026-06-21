# LogBlade Win32

LogBlade e um visualizador de logs Win32 direto em C#.

## O que ele faz

- Abre um arquivo real pela linha de comando: `LogBlade.exe <path>`.
- Cria a janela primeiro, mostra `Loading file...` e monta a primeira viewport em background.
- Usa scroll offset-based: thumb grande por offset aproximado e navegacao fina por linha descoberta sob demanda.
- Mantem somente a viewport visivel no caminho ativo; nao faz indice global por linha no startup.
- Usa Win32 + GDI para janela, pintura, scrollbar, mouse wheel e teclado.

## Build normal

Use este comando para iteracao rapida:

```powershell
.\build.ps1
```

Artefato gerado:

`artifacts\build\LogBlade.exe`

O launcher oficial do bake-off usa este artefato de build normal.

## Build de distribuicao

Use este comando so quando precisar validar o caminho distribuivel:

```powershell
.\package.ps1
```

Saida esperada:

`artifacts\publish\LogBlade.exe`

O script tenta NativeAOT quando possivel e cai para publish self-contained quando o ambiente nao consegue fechar o pipeline AOT. Essa validacao e um gate arquitetural; nao precisa rodar em toda iteracao.

## Run

```powershell
.\run.ps1 .\artifacts\sample.log
```

O `run.ps1` prefere o artefato do build normal e tambem aceita o publish de distribuicao quando ele ja existir.

## Encoding rules

- UTF-8 com BOM
- UTF-16 LE com BOM
- UTF-16 BE com BOM
- Sem BOM, cai direto em Windows-1252

## Notas

- Sem WPF, WinForms, Avalonia, MAUI ou WebView.
- O viewer desenha direto com GDI via Win32 P/Invoke.
- `Windows-1252` usa a implementacao local de [Windows1252Encoding.cs](src/LogBlade.Back/Windows1252Encoding.cs), sem depender de provider global.
- Cada execucao grava um log UTF-8 em `%LOCALAPPDATA%\LogBlade\logs`, com fallback para `%TEMP%\LogBlade\logs`.
- Retencao de logs e best-effort e mantem os 20 mais recentes.
- Falhas de startup e runtime sao fail-fast, visiveis e registradas antes do encerramento quando o log persistente esta disponivel.
