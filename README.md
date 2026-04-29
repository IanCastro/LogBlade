# C# NativeAOT Win32 Log Viewer MVP

Viewer Win32 direto em C# para o repo `mvp-csharp-nativeaot-win32`.

## O que ele faz

- Abre um arquivo real pela linha de comando: `LogViewer.exe <path>`
- Cria indice esparso com checkpoints a cada 4096 linhas
- Decodifica so as linhas visiveis sob demanda
- Mantem no maximo 300 linhas decodificadas em memoria
- Usa Win32 + GDI para janela, pintura, scrollbar, mouse wheel e teclado

## Build normal

Use este comando para iteracao rapida:

```powershell
.\build.ps1
```

Artefato gerado:

`artifacts\build\LogViewer-CSharp.exe`

O launcher oficial do bake-off usa este artefato de build normal.

## Build de distribuicao

Use este comando so quando precisar validar o caminho distribuivel:

```powershell
.\package.ps1
```

Saida esperada:

`artifacts\publish\LogViewer-CSharp.exe`

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
- Cada execucao grava um log UTF-8 em `%LOCALAPPDATA%\LogReaderMvp\mvp-csharp-nativeaot-win32\logs`, com fallback para `%TEMP%\LogReaderMvp\mvp-csharp-nativeaot-win32\logs`.
- Retencao de logs e best-effort e mantem os 20 mais recentes.
- Falhas de startup e runtime sao fail-fast, visiveis e registradas antes do encerramento quando o log persistente esta disponivel.
