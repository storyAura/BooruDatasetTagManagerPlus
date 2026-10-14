<div align="center">

# BooruDatasetTagManager+ 1.2.8

Ferramenta Windows para marcar datasets de LoRA e personagens

[简体中文](../../README.md) · [English](../../README_en.md)

[![Release](https://img.shields.io/github/v/release/storyAura/BooruDatasetTagManagerPlus?color=0078D7)](https://github.com/storyAura/BooruDatasetTagManagerPlus/releases) ![Platform](https://img.shields.io/badge/platform-Windows%20x64-0078D7) ![.NET](https://img.shields.io/badge/.NET-8-512BD4) [![License](https://img.shields.io/badge/license-MIT-2EA043)](../../LICENSE)

</div>

![BooruDatasetTagManager+](../images/promo.png)

Cada imagem tem um `.txt` com o mesmo nome para as tags: abra a pasta e edite. Também dá para marcar com o Tag tagger local ou com LLM, auditar tags de personagem e buscar tags do Danbooru em chinês. A interface começa em chinês simplificado e pode ser trocada para português. Fork de [starik222/BooruDatasetTagManager](https://github.com/starik222/BooruDatasetTagManager).

[Download](https://github.com/storyAura/BooruDatasetTagManagerPlus/releases) · [Guia de funcionalidades](FEATURES_pt_BR.md) · [Notas da versão](../RELEASE_NOTES_v1.2.8.md) · [Problemas](https://github.com/storyAura/BooruDatasetTagManagerPlus/issues)

## Funcionalidades

- **Edição de tags**: desfazer / refazer, pesos, edição em lote com seleção múltipla; "Todas as tags" conta cada tag e filtra imagens com AND / OR / NOT / XOR
- **Busca de tags em chinês**: cerca de 100 mil tags gerais e 330 mil nomes de personagens embutidos; digitar 头发 encontra `long hair` e `black hair`; as tags ganham cor por categoria e podem ser filtradas e ordenadas
- **Tag tagger**: modelos ONNX locais (WD14 / PixAI / CL / OppaiOracle) em GPU ou CPU; as imagens não saem do seu computador
- **Marcação LLM**: qualquer endpoint compatível com OpenAI, com saída em tags ou em linguagem natural
- **Auditoria de tags de personagem**: a IA compara as tags com uma imagem de referência, remove as erradas e acrescenta cores; modo esparso ou completo, até 4 personagens, nada é gravado antes da sua revisão
- **Ferramentas de imagem**: editor, recorte em lote, recortes múltiplos (incl. YOLO), pré-buckets, remoção de fundo
- **Organização do dataset**: navegador por pastas kohya, imagens semelhantes, verificação de imagens corrompidas, classificar em pastas por tag, extração de frames de vídeo
- **Linha de comando**: o mesmo exe roda sem janela para edições em lote, marcação e auditoria

## Primeiros passos

**Requisitos**: Windows 10 / 11 x64. O pacote é autocontido, sem instalação separada do .NET. Só a marcação LLM e a auditoria de personagem precisam de API; o resto funciona offline.

1. Baixe `BooruDatasetTagManagerPlus-*-win-x64.zip` em [Releases](https://github.com/storyAura/BooruDatasetTagManagerPlus/releases) e extraia
2. Execute `BooruDatasetTagManagerPlus.exe`
3. **Arquivo → Carregar Pasta**, escolha a pasta do dataset e edite as tags
4. Antes de usar recursos LLM, preencha endpoint, chave e modelo em **Configurações LLM** na barra de menus

| Para fazer | Onde |
| --- | --- |
| Marcação automática | Ferramentas → Ferramentas de marcação → Tag tagger / Marcação LLM |
| Auditar tags de personagem | Ferramentas → Ferramentas de marcação → Auditoria de tags de personagem |
| Recortar, remover fundo, buckets | Ferramentas → Ferramentas de processamento / pré-processamento |
| Scripts em lote | `BooruDatasetTagManagerPlus.exe help` |

Cada função está descrita com capturas de tela no **[guia de funcionalidades](FEATURES_pt_BR.md)**.

### Compilar a partir do código-fonte

```powershell
dotnet build BooruDatasetTagManager.sln -c Debug -f net8.0-windows
dotnet test BooruDatasetTagManager.Tests\BooruDatasetTagManager.Tests.csproj
```

`quick_build.bat` gera um build autocontido em `dist/` (baixa o FFmpeg na primeira vez).

## Histórico de versões

- **1.2.8** (atual) — O Tag tagger passa a incluir o PixAI Tagger v1.0 e o WD EVA02 2026 Canary, em GPU ou CPU; janela de busca nas tags da imagem (obrigado @furjifurji #1); a auditoria de personagem não inventa mais tags coloridas, a temperatura é ajustável e erros de rede são repetidos. [Notas da versão](../RELEASE_NOTES_v1.2.8.md)
- **1.2.7** — O Tag tagger passa a incluir o OppaiOracle; a auditoria de personagem percorre as tags uma a uma; os recortes múltiplos com YOLO não perdem mais caixas pequenas. [Notas da versão](../RELEASE_NOTES_v1.2.7.md)
- **1.2.6** — O tagger ONNX passa a se chamar Tag tagger; menu Ferramentas agrupado; carregamento de bibliotecas nativas no Win10 e memória em lotes grandes reforçados. [Notas da versão](../RELEASE_NOTES_v1.2.6.md)

Versões anteriores estão em [Releases](https://github.com/storyAura/BooruDatasetTagManagerPlus/releases) e nas notas da versão em [docs](../).

## Perguntas frequentes

- **Minhas imagens são enviadas?** O Tag tagger, a remoção de fundo e as ferramentas de vídeo rodam no seu computador. Só a marcação LLM e a auditoria de personagem enviam imagens ao endpoint configurado.
- **Onde ficam as configurações?** Em `Documentos\BooruDatasetTagManagerPlus\settings.json`, com as chaves de API criptografadas por DPAPI. As pastas `Models/` e `Cache/` ao lado do exe podem ser apagadas; os modelos podem ser baixados de novo dentro do aplicativo.
- **Como relatar um problema?** Ative o modo de depuração em Configurações → Geral, reproduza o problema e anexe `debug.log` (e `crash.log` após uma falha) da pasta do programa em uma [issue](https://github.com/storyAura/BooruDatasetTagManagerPlus/issues).

## Como contribuir

Issues e pull requests são bem-vindos. Garanta que `dotnet test` passe e acrescente todo texto novo de interface aos 5 arquivos de idioma em `BooruDatasetTagManager/Languages/`.

## Agradecimentos e licença

- **[starik222](https://github.com/starik222)** — autor do [BooruDatasetTagManager](https://github.com/starik222/BooruDatasetTagManager), sobre o qual este projeto foi construído
- **[FFmpeg](https://ffmpeg.org/)** — processamento de vídeo (componente GPL incluído nos Releases)
- Licenciado sob a [Licença MIT](../../LICENSE); mantenha os avisos de copyright do upstream ao redistribuir builds modificados
