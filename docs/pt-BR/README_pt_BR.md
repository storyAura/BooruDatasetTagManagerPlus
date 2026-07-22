# BooruDatasetTagManager+ 1.2.0

[English](../../README_en.md) | [简体中文](../../README.md)

Ferramenta para Windows de marcação de datasets de LoRA e de personagens, fork de **[starik222/BooruDatasetTagManager](https://github.com/starik222/BooruDatasetTagManager)**. Mantém o fluxo original "carregar uma pasta → editar o `.txt` correspondente" e adiciona Marcação LLM (modos Tags / Linguagem natural), auditoria de tags de personagem, marcação ONNX local e um fluxo de trabalho de tags em chinês. **O idioma padrão da interface é o chinês simplificado (zh-CN).** Licenciado sob a [Licença MIT](../../LICENSE).

![Janela principal](../images/main-window-dataset-browser.png)

## Histórico de versões

- **1.2.0** (atual) — painel do dataset reconstruído como navegador unificado por pastas (busca, recolher, renomear em lote, marcação rápida por pasta) com pré-visualização incorporada de várias imagens; cores semânticas de tags e ordenação por categoria; correspondência com o catálogo de personagens do danbooru (cores + nomes traduzidos); diversas correções de tradução, da janela da wiki e do assistente de auditoria; reforço de publicação e segurança de dados após auditoria (reversão de renomeação, token HF restrito ao huggingface.co, empacotamento isolado, trava de salvamento do LLM, proteção contra sobrescrita na conversão de vídeo, inicialização tolerante a falhas de configuração). [Notas da versão](../RELEASE_NOTES_v1.2.0.md)
- **1.1.3** — reforço de E/S de arquivos e segurança de dados (corrige os 8 riscos confirmados por uma auditoria interna: falhas de salvamento mantêm as edições, exclusão transacional, gravações concorrentes seguras, …); adiciona o editor de imagem, os modelos ONNX da família CL, a busca de tags com dicionário chinês e a ação rápida por clique duplo em Todas as tags. [Notas da versão](../RELEASE_NOTES_v1.1.3.md)
- **1.1.2** — janela unificada de Marcação LLM (modos Tags / Linguagem natural); remoção de fundo dentro do processo (RMBG-1.4); proteção contra falhas, gravações atômicas, chaves criptografadas e outros reforços de robustez/segurança. [Notas da versão](../RELEASE_NOTES_v1.1.2.md)
- **1.1.1** — salvamento mais rápido da auditoria de tags de personagem; diálogo unificado de Recortar imagem. [Notas da versão](../RELEASE_NOTES_v1.1.1.md)
- **1.1** — catálogo WD14 completo, limites por modelo, correção do PixAI. [Notas da versão](../RELEASE_NOTES_v1.1.md)
- **1.0.5** — Tagger ONNX unificado, ferramentas de vídeo. [Notas da versão](../RELEASE_NOTES_v1.0.5.md)

## Primeiros passos

Baixe `BooruDatasetTagManagerPlus-*-win-x64.zip` em [Releases](https://github.com/storyAura/BooruDatasetTagManagerPlus/releases), extraia e execute `BooruDatasetTagManagerPlus.exe` (autocontido; não requer instalação separada do .NET).

1. **Arquivo → Carregar Pasta**; *Carregar Pasta (opções de carregamento)…* permite ainda pular as miniaturas (mais rápido em datasets grandes) ou ler tags iniciais dos metadados das imagens (útil para gerações recentes ainda sem arquivos `.txt`)
2. Edite as tags diretamente: as caixas de busca de "Todas as tags" e "Tags da imagem" entendem o dicionário chinês (digitar 头发 encontra long hair, black hair, …); o clique duplo em uma linha de "Todas as tags" executa uma ação rápida (abre "Substituir em todas" por padrão, configurável nas Configurações); abra a Wiki do Danbooru para tags desconhecidas
3. Antes de usar qualquer recurso LLM, configure o endpoint compatível com OpenAI e os modelos em **Configurações LLM**
4. Execute **Ferramentas → Marcação LLM / Tagger ONNX / Remover fundo / ferramentas de vídeo**, ou **Teste → Abrir auditoria de tags**, conforme necessário

### Compilar a partir do código-fonte

```powershell
dotnet build BooruDatasetTagManager.sln -c Debug -f net8.0-windows
dotnet test BooruDatasetTagManager.Tests\BooruDatasetTagManager.Tests.csproj
dotnet publish BooruDatasetTagManager\BooruDatasetTagManager.csproj -c Release -f net8.0-windows -r win-x64 --self-contained true -o dist
```

- `test_start.bat` — inicia a versão Release (ou Debug)
- `quick_build.bat` — build local rápido para `dist/` (baixa o FFmpeg no primeiro build)

A execução local cria **Models/** (pesos ONNX baixados), **Cache/** e **settings.json** (chaves de API e preferências) ao lado do executável. Todos são dados locais gerados automaticamente e podem ser excluídos com segurança — as configurações voltam ao padrão e os modelos podem ser baixados novamente de dentro do aplicativo.

## Funcionalidades

| Módulo | Descrição |
| --- | --- |
| **Navegador do dataset** | Navegador por grupos de pastas (busca, recolher, renomear / renomear em lote, marcação rápida por pasta); pré-visualização incorporada (lado a lado na seleção múltipla); formato·pixels·tamanho na linha |
| **Semântica de tags** | Tons claros em 18 categorias e ordenação por categoria; catálogo de personagens do danbooru embutido (correspondência exata + traduções "nome (obra)") |
| **Marcação LLM** | Modos Tags / Tags→Linguagem natural; endpoint compatível com OpenAI; modelos de prompt; concorrência LLM 1–100 |
| **Auditoria de tags de personagem** | Palavra de ativação + imagem de referência + inventário do dataset; revisão por IA em duas etapas; um ou dois personagens; salvamento transacional |
| **Tagger ONNX** | Catálogo WD14 local + PixAI + família CL; limites memorizados por modelo; download do HuggingFace |
| **Remoção de fundo** | RMBG-1.4 ONNX embutido, totalmente local — sem serviço externo; fundo transparente ou de cor sólida |
| **Editor de imagem** | Pincel / borracha / conta-gotas / recorte / rotação e espelhamento com atalhos no estilo Photoshop; diálogo separado de recorte de várias regiões |
| **Ferramentas de vídeo** | Conversão de formato; extração de todos os frames / por FPS / frames específicos; FFmpeg incluído |
| **Edição de tags** | Busca com dicionário chinês, ação rápida por clique duplo em Todas as tags, revisão com seleção múltipla (Shift+T), Wiki do Danbooru |

## Guia de funcionalidades

### Navegador do dataset e pré-visualização

O painel do dataset é um navegador unificado: a caixa de busca filtra pastas e nomes de arquivo juntos; as pastas de repetição do kohya aparecem como grupos recolhíveis (datasets com várias pastas abrem totalmente recolhidos; botões de expandir/recolher tudo ficam ao lado da busca), e clicar no cabeçalho de uma pasta limita o dataset a ela (contagens de Todas as tags, operações em lote e o assistente de auditoria acompanham); as linhas de imagem mostram miniatura, nome e `formato · pixels · tamanho`, com seleção no estilo gerenciador de arquivos (Ctrl / Shift / Ctrl+A / setas / menu de contexto / Delete).

- **Clique direito na pasta**: renomear a pasta (disco + remapeamento em memória, edições não salvas sobrevivem); renomear imagens em lote (prefixo + números / letras / nome original + sufixo, prévia ao vivo, o `.txt` acompanha); marcar a pasta com ONNX / LLM
- **Pré-visualização incorporada**: painel recolhível sob o navegador (Exibir → Mostrar pré-visualização, estado persistido); a seleção múltipla mostra as quatro primeiras imagens lado a lado, clique duplo em uma célula abre no visualizador flutuante; a janela flutuante tem zoom ancorado no cursor, arrastar para deslocar, clique duplo ajustar ↔ 100 %, Ctrl+0 / Ctrl+1
- **Cores e ordenação por categoria**: os dois painéis de tags recebem tons claros em 18 categorias semânticas (personagem / obra / cabelo / olhos / roupas …); o botão *Ordenar por categoria* das tags da imagem agrupa por categoria respeitando "não ordenar as primeiras N linhas"; em Todas as tags a ordenação por categoria é opcional (desligada por padrão)
- **Catálogo de personagens**: ~330 mil tags de personagens do danbooru em `Data/danbooru_character_tags.csv` para coloração exata e traduções "nome (obra)"; pode ser desativado em Configurações → Tradução

### Marcação LLM

Entrada: **Ferramentas → Marcação LLM…**, o menu de contexto do dataset, ou o botão "Gerar tags automaticamente" na barra de ferramentas de tags. Primeiro configure o endpoint compatível com OpenAI, os modelos de texto/visão e a concorrência LLM global (padrão 5, de 1 a 100) em **Configurações LLM**.

![Configurações LLM](../images/llm-settings.png)

![Marcação LLM](../images/llm-tagger.png)

- **Modo Tags** — imagem → tags, gravadas de volta no dataset conforme o modo de gravação (substituir / acrescentar / ignorar existentes), com ordenação, prefixo/sufixo e pós-processamento de sublinhados; quatro modelos de prompt integrados (Danbooru Tag / Natural Language / Mixed Mode / Natural Language 2), e os modelos personalizados são exportados como JSON sem credenciais
- **Modo Tags → Linguagem natural** (antigo TAG2NL) — tags + imagem → uma legenda em linguagem natural; formato de saída **Tags+LN / apenas LN**; salva uma cópia em `dataset_captioned/` por padrão (o `.txt` de origem permanece somente leitura; saídas existentes podem ser ignoradas) ou grava no próprio `.txt` da imagem
- **ONNX primeiro se sem tags** — imagens sem tags são primeiro marcadas pelo tagger ONNX local e depois entregues ao LLM — um pipeline automático de tags → linguagem natural

### Auditoria de tags de personagem

Entrada: **Teste → Abrir auditoria de tags…**. Defina a palavra de ativação bloqueada (sempre mantida), o estilo de marcação (**enxuto** mantém as características centrais / **completo** mantém todos os detalhes corretos), um limite mínimo de ocorrências e uma imagem de referência; a IA executa uma triagem textual seguida de uma revisão visual (não há como voltar etapas — cancele e reabra para mudar os parâmetros); por fim, revise cada decisão (manter / excluir / substituir / incerto), pré-visualize o prompt final do personagem e **Aplicar e salvar** grava de forma transacional, com reversão em caso de falha.

Há suporte a **datasets com dois personagens**: defina palavra de ativação, imagens de referência e gênero para os personagens A / B; as imagens são atribuídas pela palavra de ativação e depois pela pasta, imagens compartilhadas recebem automaticamente tags de contagem de sujeitos (`2girls` etc.), e a revisão da IA, a revisão tag a tag e a aplicação ocorrem personagem por personagem.

![Revisão da auditoria](../images/character-tag-audit-review.png)

### Tagger ONNX

Entrada: **Ferramentas → Tagger ONNX…**, ou clique com o botão direito em **Retaguear com ONNX** nas imagens selecionadas (inicia automaticamente); o item **Marcar pasta com ONNX…** do clique direito na pasta pré-seleciona a origem *Pasta atual* e só inicia após você confirmar as configurações.

![Tagger ONNX](../images/onnx-tagger.png)

- Modelos: catálogo WD14 completo (12 modelos) + PixAI 0.9 + família CL (cl_tagger v1.02, cl_tagger_v2 v2.00 / v2.01a 🔒); limites e configurações memorizados por modelo; download do HuggingFace oficial ou do espelho
- O cl_tagger_v2 é um **repositório restrito (gated)** cuja licença do autor proíbe redistribuição e distribuição em pacotes — o aplicativo não o inclui; um aviso de licença aparece antes do download, e é preciso solicitar acesso no HuggingFace e informar o seu próprio Access Token (armazenado com criptografia DPAPI), ou colocar manualmente os arquivos baixados na pasta `Models`
- Modo de gravação (substituir / acrescentar / ignorar existentes), ordenação opcional, sublinhado→espaço, tags de prefixo/sufixo; barra de progresso para execuções em lote

### Remoção de fundo

Entrada: **Ferramentas → Remover fundo**, ou o menu de contexto do dataset. O RMBG-1.4 ONNX embutido executa totalmente no local — **sem serviço externo**; download do modelo com um clique no primeiro uso (~176 MB, ou ~44 MB quantizado; fonte oficial / espelho).

![Remoção de fundo](../images/background-removal.png)

- Escopo: todas as imagens ou apenas as selecionadas; fundo: **transparente** ou **cor sólida** (branco por padrão, com seletor de cores); "Removing test" pré-visualiza primeiro uma única imagem
- Saída: **substituir o original** ou **salvar uma cópia `_nobg.png`** (escolhas lembradas); em seguida as miniaturas são atualizadas ou as cópias são importadas automaticamente

### Editor de imagem

Entrada: menu de contexto do dataset → **Editar imagem**. Layout no estilo Photoshop: caixa de ferramentas compacta à esquerda, barra de opções no topo, barra de status embaixo.

![Editor de imagem](../images/image-editor.png)

- Atalhos consistentes com o Photoshop: **B** pincel, **E** borracha, **I** conta-gotas, **C** recorte, **H** mão (ou segure **Espaço**), `[`/`]` tamanho do pincel, **Alt+clique** amostra uma cor, zoom com a roda do mouse ancorado no cursor, **Ctrl+0** ajustar, **Ctrl+1** 100%, **Ctrl+Z / Ctrl+Shift+Z / Ctrl+Y** desfazer/refazer (um traço = um passo, até 15), **Enter** aplicar recorte, **Ctrl+S** salvar
- Salvar **sobrescreve o original** (gravação atômica — uma falha não corrompe o arquivo) ou grava uma **cópia `_edit`** (arquivo de tags clonado e importado para o dataset); a ação padrão é configurável em Configurações → UI
- Há também o diálogo **Recortar imagem** no menu de contexto do dataset: desenhe várias regiões de uma vez, exporte `_r1/_r2…` para a pasta de origem, com importação automática para o dataset

![Recorte de várias regiões](../images/crop-image-multi-region.png)

### Ferramentas de vídeo

**Ferramentas → Conversão de vídeo… / Extração de frames…**. Converta entre mp4 / mkv / avi / webm / mov / flv (com opção de substituir o original); extraia todos os frames, por FPS, no FPS nativo ou por números de frame específicos, com pré-visualização e fluxo de bloqueio de frames; os resultados são importados para o dataset. O FFmpeg vem incluído nos builds de Release.

![Extração de frames de vídeo](../images/video-frame-extraction.png)

### Revisão de tags com seleção múltipla

Selecione várias imagens e pressione **Shift+T**: a lista de tags à esquerda (com contagem de ocorrências, ordenada por frequência) troca a tag em revisão; **borda verde = tem a tag, vermelha = não tem** — clique em Y/N em uma miniatura para alternar; as edições em várias tags são aplicadas em um único salvamento.

![Editor de tags com seleção múltipla](../images/multi-select-tag-editor.png)

### Dados e privacidade

- **A Marcação LLM e a auditoria de tags de personagem enviam imagens ao endpoint que você configurou**; a marcação ONNX, a remoção de fundo e as ferramentas de vídeo executam totalmente na sua máquina
- As configurações (incluindo as chaves de API criptografadas com DPAPI) ficam no arquivo local `settings.json`; o salvamento de tags é atômico, as ferramentas em lote nunca destroem os originais e a exclusão de imagens é transacional, com reversão

## Agradecimentos e licença

- **[starik222](https://github.com/starik222)** — autor do [BooruDatasetTagManager](https://github.com/starik222/BooruDatasetTagManager), sobre o qual este projeto foi construído
- **[FFmpeg](https://ffmpeg.org/)** — processamento de vídeo (componente GPL incluído nos Releases)
- Licenciado sob a [Licença MIT](../../LICENSE); mantenha os avisos de copyright do upstream ao redistribuir builds modificados
