# BooruDatasetTagManager+ 1.2.6

[English](../../README_en.md) | [简体中文](../../README.md)

Ferramenta Windows para marcar datasets de LoRA e personagens, fork de **[starik222/BooruDatasetTagManager](https://github.com/starik222/BooruDatasetTagManager)**.

Cada imagem tem um `.txt` com o mesmo nome para as tags — abra a pasta e edite. Também dá para marcar com LLM ou o **Tag tagger** local (antes tagger ONNX; o motor continua WD14 / PixAI / CL), auditar personagens e buscar tags em chinês. A interface começa em chinês simplificado. [Licença MIT](../../LICENSE).

![Janela principal](../images/main-window-dataset-browser.png)

## Histórico de versões

- **1.2.6** (atual) — **O tagger ONNX passou a chamar-se Tag tagger** (menu Ferramentas, título da janela e clique direito; o motor continua ONNX local); o corretor de tags pode desligar substituições da família de personagem; a proporção do YOLO pode reconhecer cada imagem automaticamente; a seleção Shift/Ctrl nas tags da imagem volta a funcionar; falhas de carga nativa no Win10 e picos de memória em lotes grandes foram reforçados. O restante é sobretudo polimento de interface e fluxo: menu Ferramentas agrupado, substituição rápida numa janela própria, pré-buckets alinhados ao lote com Gradient, textos e janelas mais curtos. [Notas da versão](../RELEASE_NOTES_v1.2.6.md)
- **1.2.5** — Novidade: recorte em lote, recortes múltiplos, detecção YOLO, pré-buckets, filtro de categorias em dois níveis, classificar imagens em pastas por tag; configurações passam a ficar em Documentos. Correção: download ONNX apagava modelo bloqueado; tradução travava. [Notas da versão](../RELEASE_NOTES_v1.2.5.md)
- **1.2.4** — Correção de tags de cor erradas no WD14, nomes de arquivo longos e o seletor da auditoria multi; ONNX ordena por confiança; extração aleatória por porcentagem; ordenar o dataset por tipo de arquivo. [Notas da versão](../RELEASE_NOTES_v1.2.4.md)
- **1.2.3** — Verificador de imagens corrompidas; substituição de fundo transparente em lote (pasta / todas); correção do filtro “clicar NOT, aplicar OR”; reforço de chaves e caminhos. [Notas da versão](../RELEASE_NOTES_v1.2.3.md)
- **1.2.2** — Imagens semelhantes, auditoria multi (até 4), filtro por categoria, visão plana, corretor de tags, CLI, F5 para recarregar; correção do modo “ignorar tags existentes” que gastava créditos LLM. [Notas da versão](../RELEASE_NOTES_v1.2.2.md)
- **1.2.1** — Backend Python removido; seleção de várias pastas; repetir só o personagem que falhou na auditoria; carregamento inicial mais rápido e reforço de segurança. [Notas da versão](../RELEASE_NOTES_v1.2.1.md)
- **1.2.0** — Navegador por pastas com pré-visualização; cores e ordenação por categoria; catálogo de personagens do danbooru. [Notas da versão](../RELEASE_NOTES_v1.2.0.md)
- **1.1.3** — Editor de imagem, ONNX da família CL, busca com dicionário chinês; falhas de salvamento não perdem edições. [Notas da versão](../RELEASE_NOTES_v1.1.3.md)
- **1.1.2** — Janela unificada de Marcação LLM; remoção de fundo RMBG local; proteção contra falhas e chaves criptografadas. [Notas da versão](../RELEASE_NOTES_v1.1.2.md)
- **1.1.1** — Salvamento mais rápido da auditoria; diálogo unificado de recorte. [Notas da versão](../RELEASE_NOTES_v1.1.1.md)
- **1.1** — Catálogo WD14 completo, limites por modelo, correção do PixAI. [Notas da versão](../RELEASE_NOTES_v1.1.md)
- **1.0.5** — Tagger ONNX unificado, ferramentas de vídeo. [Notas da versão](../RELEASE_NOTES_v1.0.5.md)

## Primeiros passos

Baixe `BooruDatasetTagManagerPlus-*-win-x64.zip` em [Releases](https://github.com/storyAura/BooruDatasetTagManagerPlus/releases), extraia e execute `BooruDatasetTagManagerPlus.exe` (autocontido; não requer instalação separada do .NET).

1. **Arquivo → Carregar Pasta**; *Carregar Pasta (opções de carregamento)…* permite ainda pular as miniaturas (mais rápido em datasets grandes) ou ler tags iniciais dos metadados das imagens (útil para gerações recentes ainda sem arquivos `.txt`); *Recarregar o dataset atual* (F5) atualiza a pasta carregada a partir do disco a qualquer momento
2. Edite as tags diretamente: as caixas de busca de "Todas as tags" e "Tags da imagem" entendem o dicionário chinês (digitar 头发 encontra long hair, black hair, …); Shift/Ctrl nas tags da imagem aplica exclusão, cópia e peso a todas as linhas selecionadas; o clique duplo em uma linha de "Todas as tags" executa uma ação rápida (abre "Substituir em todas" por padrão, configurável nas Configurações); abra a Wiki do Danbooru para tags desconhecidas
3. Antes de usar qualquer recurso LLM, configure o endpoint compatível com OpenAI e os modelos em **Configurações LLM**
4. Abra **Ferramentas** conforme necessário: **Ferramentas de processamento** (substituir fundo transparente / ferramentas de vídeo / remover fundo / recorte em lote / recortes múltiplos / detectar YOLO), **Ferramentas de marcação** (Tag tagger / marcação LLM / auditoria de tags de personagem / substituição rápida), **Ferramentas de pré-processamento** (encontrar imagens semelhantes / verificar imagens corrompidas / classificar em pastas por tag / pré-buckets); a janela **Funções de teste** ainda abriga o corretor de tags inconsistentes
5. Scripts de automação podem usar o mesmo exe pela linha de comando: `BooruDatasetTagManagerPlus.exe help` lista todos os comandos (estatísticas / edições em lote / exportação / fix-tags / onnx-tag / audit)

### Compilar a partir do código-fonte

```powershell
dotnet build BooruDatasetTagManager.sln -c Debug -f net8.0-windows
dotnet test BooruDatasetTagManager.Tests\BooruDatasetTagManager.Tests.csproj
dotnet publish BooruDatasetTagManager\BooruDatasetTagManager.csproj -c Release -f net8.0-windows -r win-x64 --self-contained true -o dist
```

- `test_start.bat` — inicia a versão Release (ou Debug)
- `quick_build.bat` — build local rápido para `dist/` (baixa o FFmpeg no primeiro build)

A execução local cria **Models/** (pesos ONNX baixados) e **Cache/** ao lado do executável. O **settings.json** (chaves de API e preferências) fica em `Documentos\BooruDatasetTagManagerPlus` para Debug / Release / dist compartilharem a mesma config; se Documentos ainda não tiver arquivo, ele é copiado de ao lado do exe; se Documentos já tiver uma config vazia e o arquivo ao lado do exe ainda tiver uma API reconhecível (endpoint / chaves / perfis), só esses campos de API são mesclados. Todos são dados locais gerados automaticamente e podem ser excluídos com segurança — as configurações voltam ao padrão e os modelos podem ser baixados novamente de dentro do aplicativo.

## Funcionalidades

| Grupo | Inclui |
| --- | --- |
| **Marcação** | LLM (tags / legendas) · **Tag tagger** (ONNX local: WD14 / PixAI / CL) · auditoria de personagem (até 4) |
| **Tags** | Busca em chinês, cores e filtro L1/L2, corretor de tags, filtrar imagens por tag |
| **Imagens** | Editor, recorte em lote, recortes múltiplos (incl. YOLO), pré-buckets, remover / preencher fundo |
| **Organização** | Navegador + pré-visualização, classificar em pastas por tag, buckets por resolução, semelhantes, imagens corrompidas, vídeo / frames |
| **CLI** | O mesmo exe: estatísticas, edições em lote, exportação, Tag tagger (`onnx-tag`), auditoria |

## Guia de funcionalidades

### Navegador do dataset e pré-visualização

O painel do dataset é um navegador unificado: a caixa de busca filtra pastas e nomes de arquivo juntos; as pastas de repetição do kohya aparecem como grupos recolhíveis (datasets com várias pastas abrem totalmente recolhidos; botões de expandir/recolher tudo, visão plana e ordenação ficam ao lado da busca; a visão plana ignora os grupos de pastas e mostra o escopo + filtro atual como uma lista única), e clicar no cabeçalho de uma pasta limita o dataset a ela (contagens de Todas as tags, operações em lote e o assistente de auditoria acompanham); as linhas de imagem mostram miniatura, nome e `formato · pixels · tamanho`, com seleção no estilo gerenciador de arquivos (Ctrl / Shift / Ctrl+A / setas / menu de contexto / Delete).

- **Ordenação**: o botão ao lado da busca ordena por nome, tipo ou data de alteração. O tipo agrupa por extensão (jpg / jpeg juntos; png, webp, mp4, webm, … cada um no seu grupo — imagens e vídeos seguem a mesma regra), depois por nome dentro do grupo; a escolha é lembrada
- **Clique direito na pasta**: renomear a pasta (disco + remapeamento em memória, edições não salvas sobrevivem); **F2** ou **clique duplo** no cabeçalho do grupo também renomeia; renomear imagens em lote (prefixo + números / letras / nome original + sufixo, prévia ao vivo, o `.txt` acompanha); **Marcar pasta com Tag tagger…** / marcar a pasta com LLM
- **Pré-visualização incorporada**: painel recolhível sob o navegador (Exibir → Mostrar pré-visualização, estado persistido); a seleção múltipla mostra as quatro primeiras imagens lado a lado, clique duplo em uma célula abre no visualizador flutuante; a janela flutuante tem zoom ancorado no cursor, arrastar para deslocar, clique duplo ajustar ↔ 100 %, Ctrl+0 / Ctrl+1
- **Cores e ordenação por categoria**: os dois painéis de tags coloram e agrupam pela categoria **primária** (cabelo / roupas / personagem …); o botão *Ordenar por categoria* das tags da imagem é uma alternância persistente: marcado, cada imagem recém-selecionada é agrupada pela primária automaticamente (respeitando "não ordenar as primeiras N linhas"); em Todas as tags a ordenação por categoria é opcional (desligada por padrão)
- **Filtro por categoria**: cada painel tem um menu de duas colunas. Marque uma primária à esquerda para filtrar o grupo inteiro; passar o mouse lista as secundárias à direita. Dá para marcar várias e buscar pelo nome. Soma-se à busca e ao filtro de contagem
- **Catálogo geral**: ~100 mil tags gerais do danbooru em `Data/danbooru_dataset_general.csv` com L1/L2, carregado na inicialização; `long_hair` e `long hair` encontram a mesma entrada. Tags desconhecidas vão para Geral
- **Catálogo de personagens**: ~330 mil tags de personagem do danbooru em `Data/danbooru_character_tags.csv` (incluindo ~26 mil relações pai/filho reais) para colorir personagens com precisão, traduções "nome (obra)" e o agrupamento familiar do corretor de tags; pode ser desligado em Configurações → Tradução. O CSV geral não tem nomes de personagem — o balde Personagem ainda vem desta tabela / da coluna de tipo Danbooru

![Filtro de categorias](../images/tag-category-filter.png)

### Menu Ferramentas

**Ferramentas** é um menu plano dividido em três barras com fundo claro (não clicáveis; a mistura é a mesma das cores de categoria de tags, legível no tema claro e no escuro):

- **Ferramentas de processamento**: substituir fundo transparente, conversão de vídeo, extração de frames, remover fundo, recorte em lote, recortes múltiplos, detectar YOLO
- **Ferramentas de marcação**: Tag tagger, marcação LLM, auditoria de tags de personagem, substituição rápida
- **Ferramentas de pré-processamento**: encontrar imagens semelhantes, verificar imagens corrompidas, classificar em pastas por tag, pré-buckets

O corretor de tags inconsistentes continua na janela **Funções de teste**; a auditoria de tags de personagem abre pelas ferramentas de marcação ou por essa janela.

### Marcação LLM

Entrada: **Ferramentas → Marcação LLM…**, o menu de contexto do dataset, ou o botão "Gerar tags automaticamente" na barra de ferramentas de tags. Primeiro configure o endpoint compatível com OpenAI, os modelos de texto/visão e a concorrência LLM global (padrão 5, de 1 a 100) em **Configurações LLM**.

![Configurações LLM](../images/llm-settings.png)

![Marcação LLM](../images/llm-tagger.png)

- **Modo Tags** — imagem → tags, gravadas de volta no dataset conforme o modo de gravação (substituir / acrescentar / ignorar existentes), com ordenação, prefixo/sufixo e pós-processamento de sublinhados; quatro modelos de prompt integrados (Danbooru Tag / Natural Language / Mixed Mode / Natural Language 2), e os modelos personalizados são exportados como JSON sem credenciais
- **Modo Tags → Linguagem natural** (antigo TAG2NL) — tags + imagem → uma legenda em linguagem natural; formato de saída **Tags+LN / apenas LN**; salva uma cópia em `dataset_captioned/` por padrão (o `.txt` de origem permanece somente leitura; saídas existentes podem ser ignoradas) ou grava no próprio `.txt` da imagem
- **Marcar com Tag tagger primeiro se sem tags** — imagens sem tags passam primeiro pelo **Tag tagger** (os mesmos modelos ONNX locais) e depois pelo LLM — um pipeline automático de tags → linguagem natural

### Auditoria de tags de personagem

Entrada: **Ferramentas → Auditoria de tags de personagem…** (a janela **Funções de teste** ainda tem a mesma entrada). Defina a palavra de ativação bloqueada (sempre mantida), o estilo de marcação (**enxuto** mantém as características centrais / **completo** mantém todos os detalhes corretos), um limite mínimo de ocorrências e uma imagem de referência; a IA executa uma triagem textual seguida de uma revisão visual (não há como voltar etapas — cancele e reabra para mudar os parâmetros); por fim, revise cada decisão (manter / excluir / substituir / incerto), pré-visualize o prompt final do personagem e **Aplicar e salvar** grava de forma transacional, com reversão em caso de falha.

Há suporte a **datasets com vários personagens** (até 4): escolha o modo de sujeito Duplo ou Múltiplo e defina palavra de ativação, imagem de referência e gênero para cada personagem (linhas vazias são ignoradas, então datasets de três personagens também funcionam); as imagens são atribuídas pela palavra de ativação e depois pela pasta, imagens compartilhadas recebem automaticamente tags de contagem de sujeitos (`2girls`, `multiple girls` etc.), a revisão da IA, a revisão tag a tag e a aplicação ocorrem personagem por personagem, e um personagem que falhou pode ser repetido sozinho (os resultados dos personagens concluídos são mantidos).

![Revisão da auditoria](../images/character-tag-audit-review.png)

### Tag tagger

Antes chamava-se **tagger ONNX**. A partir da 1.2.6 a interface passa a dizer **Tag tagger** (chinês: **Tag 推标**) no menu **Ferramentas → Marcação**, no título da janela, no clique direito **Retaguear com Tag tagger** e em **Marcar pasta com Tag tagger…**. O motor não mudou: ONNX local (WD14 / PixAI / CL), pesos em `Models/`, e os verbos de CLI continuam `onnx-tag` / `onnx-models`.

Entrada: **Ferramentas → Tag tagger…**, ou clique com o botão direito em **Retaguear com Tag tagger** nas imagens selecionadas (inicia automaticamente); o item **Marcar pasta com Tag tagger…** do clique direito na pasta pré-seleciona a origem *Pasta atual* e só inicia após você confirmar as configurações.

![Tag tagger](../images/onnx-tagger.png)

- Modelos: catálogo WD14 completo (12 modelos) + PixAI 0.9 + família CL (cl_tagger v1.02, cl_tagger_v2 v2.00 / v2.01a 🔒); limites e configurações memorizados por modelo; download do HuggingFace oficial ou do espelho
- Depois do download o app verifica o modelo; um arquivo brevemente bloqueado pelo antivírus/indexador é retentado e mantido, não tratado como corrompido e apagado. Falta de runtime nativo e outros erros de ambiente também deixam o download concluído no lugar
- O cl_tagger_v2 é um **repositório restrito (gated)** cuja licença do autor proíbe redistribuição e distribuição em pacotes — o aplicativo não o inclui; um aviso de licença aparece antes do download, e é preciso solicitar acesso no HuggingFace e informar o seu próprio Access Token (armazenado com criptografia DPAPI), ou colocar manualmente os arquivos baixados na pasta `Models`
- Modo de gravação (substituir / acrescentar / ignorar existentes), ordenação opcional, sublinhado→espaço, tags de prefixo/sufixo; barra de progresso para execuções em lote; o modo "Ignorando listas de tags existentes" pula imagens já marcadas antes da inferência e informa as contagens de gravadas / puladas ao concluir

### Substituição rápida

Entrada: **Ferramentas → Substituição rápida…**. Escolha a tag que fica à esquerda, defina o limite, veja à direita as tags do mesmo sufixo com poucas ocorrências e leia o estado curto no canto inferior esquerdo antes de confirmar a substituição no dataset. "Mesma categoria" é a última palavra (`black shoes` → `shoes`); só entram tags abaixo do limite.

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
- **Recorte em lote**: menu de contexto do dataset ou **Ferramentas → Recorte em lote**; desenhe um retângulo na imagem de referência (trave 1:1 / 2:3 / 16:9 / … ou informe uma proporção) e aplique o mesmo recorte em pixels a todas as imagens dessa resolução; sobrescreva ou salve cópias `_crop` (os arquivos de tags são clonados)

![Recorte de várias regiões](../images/crop-image-multi-region.png)

### Ferramenta de recortes múltiplos

Entrada: menu de contexto do dataset, menu do cabeçalho da pasta, ou **Ferramentas → Recortes múltiplos…**. Não é preciso clicar numa imagem antes: a origem pode ser **imagens selecionadas**, **pasta atual** ou **todas as imagens**. À esquerda, a pré-visualização mostra as caixas de corte da primeira imagem (o modo YOLO não detecta aqui). Comprime imagens grandes para degraus de treino, ou corta na proporção escolhida, preservando o máximo de detalhe de alta frequência. **Os originais nunca são sobrescritos**; cada degrau marcado grava um arquivo novo, clona as tags e importa para o dataset.

- **Só redimensionar**: mantém a proporção original e reduz o lado maior a cada degrau marcado
- **Recorte central na proporção**: pega o maior retângulo centrado 1:1 / 2:3 / 16:9 / … e depois reduz
- **Fatiar em blocos**: espalha janelas do tamanho do degrau nos pixels de origem (última linha/coluna encostada na borda); só reduz se a fatia ainda for maior que o degrau
- **Recorte em posição aleatória**: N recortes por imagem (padrão 1, máximo 32); o retângulo da proporção cai de forma uniforme na faixa deslizante restante e depois reduz
- **Recorte YOLO**: escolha um detector de anime deepghs na lista — **Person** (v1.1 n/s/m, v1.2 s, v1.3 s; padrão v1.1 small), **Face** (v1.3 s, v1.4 n/s), **Head** (v1.6 s, v2.0 n/s); MIT, não gated, YOLOv8 ONNX padrão. Cada caixa é expandida para a proporção escolhida e depois reduzida; imagens sem acerto são ignoradas. Também dá para importar o seu próprio ONNX YOLOv8; a fonte de download é a mesma do **Tag tagger** (HuggingFace / hf-mirror)
- Degraus padrão 512 / 768 / 896 / 1024 / 1280 / 1536, com multi-seleção e valores personalizados 64–8192 (alinhados para baixo em múltiplos de 64); redução Lanczos sem ampliar; imagens já menores que o degrau são ignoradas
- Também **Ferramentas → Detectar YOLO…**: janela à parte desenha as caixas, deixa manter/descartar, opcionalmente **Abrir no Tag tagger** para os recortes mantidos e depois exporta; a mesma lista de modelos, fonte de download e o botão *Baixar modelo* estão lá. A proporção padrão é **Automático** (preset mais próximo da largura/altura de cada imagem: 1:1 / 2:3 / 16:9 / …); ainda dá para travar uma proporção

### Pré-buckets

Entrada: menu de contexto do dataset, menu do cabeçalho da pasta, ou **Ferramentas → Pré-buckets…**. A origem é a pasta atual ou todas as imagens. Defina resolução / lado mínimo·máximo / passo; depois **preenche** cada imagem com bordas brancas até o tamanho exato e grava numa pasta `{largura}x{altura}` no dataset atual. As tags são clonadas. Depois de gravar, as imagens de origem e as pastas vazias são removidas.

- **Por quê**: cada bucket de resolução tem o próprio lote, então muitos buckets com sobras empurram os passos reais bem acima do valor teórico. Encaixar as imagens em menos tamanhos fixos faz o treino usar a quantidade que você escolheu
- **Configurações de buckets**: resolução (padrão 1536×1536), lado mínimo / máximo e passo (geralmente 64). *Não ampliar (só preenchimento)* vem ligado — imagens pequenas só ganham bordas brancas
- **Número alvo**: digite um número, ou toque 4 / 8 / 12 / 16. 0 mantém cada bucket pela proporção; N junta proporções vizinhas até N pastas
- **Alinhamento**: copia imagens até cada bucket ser divisível pelo lote; a coluna mostra **quantas extras** (23 + lote 5 → +2, total 25). Não alinha por lote × Gradient
- **Estimativa de passos**: repetições / lote / Gradient / épocas geram a contagem **teórica** e a **real**. Gradient só muda os passos do otimizador: lote 2 e Gradient 2 é BS efetivo 4, mas cada passo ainda lê 2 imagens

### Ferramentas de vídeo

**Ferramentas → Conversão de vídeo… / Extração de frames…**. Converta entre mp4 / mkv / avi / webm / mov / flv (com opção de substituir o original); extraia todos os frames, por FPS, no FPS nativo, por números de frame específicos ou uma porcentagem aleatória (distribuída pelo clipe ou um bloco regional contínuo; controle padrão 10%), com pré-visualização e fluxo de bloqueio de frames; os resultados são importados para o dataset. O FFmpeg vem incluído nos builds de Release.

![Extração de frames de vídeo](../images/video-frame-extraction.png)

### Revisão de tags com seleção múltipla

Selecione várias imagens e pressione **Shift+T**: a lista de tags à esquerda (com contagem de ocorrências, ordenada por frequência) troca a tag em revisão; **borda verde = tem a tag, vermelha = não tem** — clique em Y/N em uma miniatura para alternar; as edições em várias tags são aplicadas em um único salvamento.

![Editor de tags com seleção múltipla](../images/multi-select-tag-editor.png)

### Classificar em pastas por tag

Entrada: **Ferramentas → Classificar em pastas por tag…**. Marque tags, confirme, e as imagens que têm **todas** as tags marcadas são **movidas** para uma pasta na raiz — os arquivos `.txt` / `.caption` acompanham.

- **Regras**: a imagem precisa ter todas as tags marcadas; se faltar alguma, fica no lugar
- **Nome da pasta**: digite um, ou deixe em branco para `Mix`. Se o nome já existir, vira `Mix_2`, depois `Mix_3`
- **Escopo**: todas as imagens ou a pasta atual. A lista de tags tem busca; a prévia mostra quantas imagens vão ser movidas
- **Renomear**: depois, **F2** ou **clique duplo** no cabeçalho do grupo (ou clique direito para renomear) para um nome estilo kohya `10_miku`

### Localizador de imagens semelhantes

Entrada: **Ferramentas → Encontrar imagens semelhantes…**. Hash perceptual no espírito do [czkawka](https://github.com/qarmin/czkawka) (dHash + distância de Hamming), calculado direto das miniaturas em memória — milhares de imagens terminam em segundos; com uma pasta em escopo, apenas ela é varrida, e vídeos são ignorados.

- Quatro níveis de similaridade (muito alta / alta / média / baixa); resultados agrupados; **borda verde = manter, vermelha = excluir** — clique esquerdo alterna, clique direito abre o original em tamanho completo, dicas mostram nome e tamanho do arquivo, e um controle deslizante ajusta o tamanho das miniaturas
- **Manter uma por grupo (maior arquivo)** marca o restante para exclusão em um clique (heurística padrão do czkawka); cada marca ainda pode ser ajustada manualmente
- **Excluir imagens marcadas em vermelho** usa a mesma exclusão transacional da janela principal (imagem e arquivo de tags são apagados juntos, com restauração em caso de falha) e depois varre novamente de forma automática

### Verificador de imagens corrompidas

Entrada: **Ferramentas → Verificar imagens corrompidas…**. Percorre o dataset carregado (respeitando o escopo de pastas) e tenta decodificar cada imagem — aponta arquivos danificados, vazios ou ausentes; vídeos são ignorados.

- Resultados numa parede de revisão; **verde = manter, vermelho = excluir** (por padrão todos marcados para excluir); clique esquerdo alterna; dicas mostram nome e motivo; controle deslizante ajusta o tamanho das miniaturas
- **Excluir imagens marcadas em vermelho** usa a mesma exclusão transacional da janela principal e depois varre novamente

### Corretor de tags inconsistentes

Entrada: a janela do menu **Funções de teste**, grupo "Correção de tags inconsistentes". Ele varre o dataset atual (ou o escopo da pasta ativa), lista cada mudança planejada como uma prévia "imagem / remover / manter / motivo" e só aplica após confirmação — como edições normais (desfazer por imagem funciona, nada é salvo automaticamente).

- **Conflitos de contagem de pessoas**: `1boy` ao lado de `2boys` remove a contagem menor (a maior por gênero sobrevive); `solo` em imagens com várias pessoas também é removido, enquanto o semanticamente diferente `solo focus` nunca é tocado
- **Duplicatas pai-filho de personagens** (caixa no módulo Teste; ligada por padrão): quando várias tags da mesma família de personagem aparecem na mesma imagem, as contagens do dataset votam no sobrevivente; as famílias vêm das relações pai-filho reais do catálogo (variantes renomeadas como `racing miku` ↔ `hatsune miku` são pareadas, e personagens diferentes que só compartilham o nome-base nunca são mesclados). Desmarque para não mexer nos nomes de personagem; conflitos de contagem e `solo` continuam
- **Mesclar tags filhas raras** (disponível com a correção de família ligada; desligada por padrão): quando ativada, uma variante filha com menos ocorrências no dataset do que o limite (padrão 30) não é confiável e é incorporada ao ancestral confiável mais próximo — variantes raras e dispersas se consolidam na tag principal para um treinamento mais focado. Desligar a correção de família também desliga isto. A tabela de prévia permite marcar quais linhas aplicar.

### Linha de comando (CLI)

O próprio `BooruDatasetTagManagerPlus.exe` é uma ferramenta de linha de comando: um primeiro argumento conhecido executa sem janela (saída redirecionável; códigos de saída 0/1/2 = ok / erro / uso), qualquer outra coisa abre a interface como sempre. `help` mostra o uso completo:

- **Operações de dataset**: `stats`; consultas `list-images` / `list-tags` / `classify-tags` (filtro por tags, categoria L1/L2, contagem; `--category` aceita `头发` ou `Hair`, ou `头发/发色` para uma secundária); edições em lote `add-tags` / `remove-tags` / `replace-tag` (alvo condicional, `--dry-run`); `export` para JSON
- **`fix-tags`**: o gêmeo em CLI do corretor — `--no-character-variants` ignora substituições da família de personagem, `--child-threshold` define o limite de confiança (padrão 0 = desligado; sem efeito com `--no-character-variants`), `--catalog` aponta para um CSV de relações personalizado
- **`onnx-models` / `onnx-tag`**: versão em linha de comando do **Tag tagger** (ONNX local) — lista / baixa modelos automaticamente (`--hf-token` para repositórios restritos), limites e modos de gravação com a mesma semântica da interface, "ignorar existentes" filtra antes da inferência. Os verbos não mudaram, para scripts antigos continuarem a funcionar
- **`audit`**: a auditoria LLM de tags de personagem — reutiliza a configuração de API e os prompts salvos na interface, executa a revisão em duas etapas e grava de forma transacional; `--report` emite um relatório JSON, `--dry-run` só mostra as decisões
- Toda gravação é uma substituição atômica; o formato de tags (separadas por vírgula, minúsculas, sem duplicatas) é o mesmo da interface, então CLI e edições manuais se misturam livremente

### Dados e privacidade

- **A Marcação LLM e a auditoria de tags de personagem enviam imagens ao endpoint que você configurou**; o **Tag tagger**, a remoção de fundo e as ferramentas de vídeo executam totalmente na sua máquina
- O arquivo de **configurações** `settings.json` (preferências da interface + LLM/API, chaves com DPAPI) fica em `Documentos\BooruDatasetTagManagerPlus`; Configurações → Geral mostra o caminho. Debug / Release / `dist` leem o mesmo arquivo; se Documentos ainda não tiver, o arquivo ao lado do exe é copiado (incluindo `.bak`); se Documentos já existir sem API, mas o arquivo ao lado do exe ainda tiver endpoint ou chaves reconhecíveis, só esses campos de API são mesclados. O arquivo antigo não é apagado. Em outro PC as chaves precisam ser digitadas de novo
- O salvamento de tags é atômico, as ferramentas de imagem em lote gravam em arquivo temporário e só substituem em caso de sucesso, e a exclusão é feita em etapas, com restauração automática se falhar no meio. Atenção: a conversão de vídeo com "substituir o original" marcado apaga o vídeo de origem após uma conversão bem-sucedida
- O **modo de depuração** (Configurações → Geral, desligado por padrão) mostra um menu Debug e grava informações de execução e exceções no `debug.log` ao lado do executável (o menu abre o arquivo diretamente) — útil para anexar ao relatar problemas

## Agradecimentos e licença

- **[starik222](https://github.com/starik222)** — autor do [BooruDatasetTagManager](https://github.com/starik222/BooruDatasetTagManager), sobre o qual este projeto foi construído
- **[FFmpeg](https://ffmpeg.org/)** — processamento de vídeo (componente GPL incluído nos Releases)
- Licenciado sob a [Licença MIT](../../LICENSE); mantenha os avisos de copyright do upstream ao redistribuir builds modificados
