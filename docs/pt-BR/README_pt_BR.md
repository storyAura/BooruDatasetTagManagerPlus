# BooruDatasetTagManager+ 1.2.2

[English](../../README_en.md) | [简体中文](../../README.md)

Ferramenta para Windows de marcação de datasets de LoRA e de personagens, fork de **[starik222/BooruDatasetTagManager](https://github.com/starik222/BooruDatasetTagManager)**. Mantém o fluxo original "carregar uma pasta → editar o `.txt` correspondente" e adiciona Marcação LLM (modos Tags / Linguagem natural), auditoria de tags de personagem, marcação ONNX local e um fluxo de trabalho de tags em chinês. **O idioma padrão da interface é o chinês simplificado (zh-CN).** Licenciado sob a [Licença MIT](../../LICENSE).

![Janela principal](../images/main-window-dataset-browser.png)

## Histórico de versões

- **1.2.2** (atual) — Novidades: localizador de imagens semelhantes (limpeza de duplicatas por grupo), auditoria de tags multi-personagem (até 4), filtros por categoria nos painéis de tags, visão plana do dataset, corretor de tags inconsistentes, CLI completa, Recarregar o dataset atual (F5). Correções: o modo "Ignorando listas de tags existentes" não descarta mais resultados em silêncio nem desperdiça créditos de LLM (P0), a cascata de falhas do Ctrl+Z ao desfazer, falhas de referência nula sem dataset carregado, tags residuais ao trocar de pasta, entre outras. [Notas da versão](../RELEASE_NOTES_v1.2.2.md)
- **1.2.1** — segunda leva de correções da auditoria: reforço de memória e segurança de dados, primeiro carregamento mais rápido, acessibilidade e i18n completos; backend Python legado removido (configurações antigas migram automaticamente); escopo do grupo raiz e união de várias pastas no navegador do dataset; checkpoints na auditoria de dois personagens; modo de depuração opcional; correções para a pré-visualização cobrindo diálogos, dessincronização da lista de tags, cortes em DPI alto e mais. [Notas da versão](../RELEASE_NOTES_v1.2.1.md)
- **1.2.0** — painel do dataset reconstruído: navegador por grupos de pastas com pré-visualização incorporada; cores semânticas de tags e ordenação por categoria; correspondência com o catálogo de personagens do danbooru (cores + nomes traduzidos); reforço de publicação e segurança de dados pós-auditoria. [Notas da versão](../RELEASE_NOTES_v1.2.0.md)
- **1.1.3** — reforço de E/S de arquivos e segurança de dados (corrige os 8 riscos confirmados por uma auditoria interna: falhas de salvamento mantêm as edições, exclusão transacional, gravações concorrentes seguras, …); adiciona o editor de imagem, os modelos ONNX da família CL, a busca de tags com dicionário chinês e a ação rápida por clique duplo em Todas as tags. [Notas da versão](../RELEASE_NOTES_v1.1.3.md)
- **1.1.2** — janela unificada de Marcação LLM (modos Tags / Linguagem natural); remoção de fundo dentro do processo (RMBG-1.4); proteção contra falhas, gravações atômicas, chaves criptografadas e outros reforços de robustez/segurança. [Notas da versão](../RELEASE_NOTES_v1.1.2.md)
- **1.1.1** — salvamento mais rápido da auditoria de tags de personagem; diálogo unificado de Recortar imagem. [Notas da versão](../RELEASE_NOTES_v1.1.1.md)
- **1.1** — catálogo WD14 completo, limites por modelo, correção do PixAI. [Notas da versão](../RELEASE_NOTES_v1.1.md)
- **1.0.5** — Tagger ONNX unificado, ferramentas de vídeo. [Notas da versão](../RELEASE_NOTES_v1.0.5.md)

## Primeiros passos

Baixe `BooruDatasetTagManagerPlus-*-win-x64.zip` em [Releases](https://github.com/storyAura/BooruDatasetTagManagerPlus/releases), extraia e execute `BooruDatasetTagManagerPlus.exe` (autocontido; não requer instalação separada do .NET).

1. **Arquivo → Carregar Pasta**; *Carregar Pasta (opções de carregamento)…* permite ainda pular as miniaturas (mais rápido em datasets grandes) ou ler tags iniciais dos metadados das imagens (útil para gerações recentes ainda sem arquivos `.txt`); *Recarregar o dataset atual* (F5) atualiza a pasta carregada a partir do disco a qualquer momento
2. Edite as tags diretamente: as caixas de busca de "Todas as tags" e "Tags da imagem" entendem o dicionário chinês (digitar 头发 encontra long hair, black hair, …); o clique duplo em uma linha de "Todas as tags" executa uma ação rápida (abre "Substituir em todas" por padrão, configurável nas Configurações); abra a Wiki do Danbooru para tags desconhecidas
3. Antes de usar qualquer recurso LLM, configure o endpoint compatível com OpenAI e os modelos em **Configurações LLM**
4. Execute **Ferramentas → Marcação LLM / Tagger ONNX / Remover fundo / ferramentas de vídeo / Encontrar imagens semelhantes**, ou **Teste → Abrir auditoria de tags** (a auditoria e o corretor de tags inconsistentes moram lá), conforme necessário
5. Scripts de automação podem usar o mesmo exe pela linha de comando: `BooruDatasetTagManagerPlus.exe help` lista todos os comandos (estatísticas / edições em lote / exportação / fix-tags / onnx-tag / audit)

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
| **Navegador do dataset** | Navegador por grupos de pastas (busca, recolher, renomear / renomear em lote, marcação rápida por pasta); visão plana (ignora pastas, lista única); pré-visualização incorporada (lado a lado na seleção múltipla); formato·pixels·tamanho na linha |
| **Semântica de tags** | Tons claros em 18 categorias, ordenação e filtro por categoria; catálogo de personagens do danbooru embutido (correspondência exata + traduções "nome (obra)" + relações pai-filho) |
| **Marcação LLM** | Modos Tags / Tags→Linguagem natural; endpoint compatível com OpenAI; modelos de prompt; concorrência LLM 1–100 |
| **Auditoria de tags de personagem** | Palavra de ativação + imagem de referência + inventário do dataset; revisão por IA em duas etapas; um ou vários personagens (até 4); salvamento transacional |
| **Tagger ONNX** | Catálogo WD14 local + PixAI + família CL; limites memorizados por modelo; download do HuggingFace |
| **Remoção de fundo** | RMBG-1.4 ONNX embutido, totalmente local — sem serviço externo; fundo transparente ou de cor sólida |
| **Editor de imagem** | Pincel / borracha / conta-gotas / recorte / rotação e espelhamento com atalhos no estilo Photoshop; diálogo separado de recorte de várias regiões |
| **Imagens semelhantes** | Busca de duplicadas com hash perceptual no estilo czkawka (4 níveis de similaridade); revisão em grupos manter/excluir; manter uma por grupo; exclusão transacional |
| **Correção de tags** | Conflitos de contagem de pessoas / solo com várias pessoas / duplicatas pai-filho de personagens limpas de uma vez; limite de confiança (padrão 30); prévia + desfazer |
| **Ferramentas de vídeo** | Conversão de formato; extração de todos os frames / por FPS / frames específicos; FFmpeg incluído |
| **Edição de tags** | Busca com dicionário chinês, ação rápida por clique duplo em Todas as tags, revisão com seleção múltipla (Shift+T), Wiki do Danbooru |
| **CLI** | O mesmo exe, sem janela: estatísticas / edições em lote / exportação / fix-tags / marcação ONNX / auditoria LLM para automação |

## Guia de funcionalidades

### Navegador do dataset e pré-visualização

O painel do dataset é um navegador unificado: a caixa de busca filtra pastas e nomes de arquivo juntos; as pastas de repetição do kohya aparecem como grupos recolhíveis (datasets com várias pastas abrem totalmente recolhidos; botões de expandir/recolher tudo e de visão plana ficam ao lado da busca — a visão plana ignora os grupos de pastas e mostra o escopo + filtro atual como uma lista única, com estado persistido), e clicar no cabeçalho de uma pasta limita o dataset a ela (contagens de Todas as tags, operações em lote e o assistente de auditoria acompanham); as linhas de imagem mostram miniatura, nome e `formato · pixels · tamanho`, com seleção no estilo gerenciador de arquivos (Ctrl / Shift / Ctrl+A / setas / menu de contexto / Delete).

- **Clique direito na pasta**: renomear a pasta (disco + remapeamento em memória, edições não salvas sobrevivem); renomear imagens em lote (prefixo + números / letras / nome original + sufixo, prévia ao vivo, o `.txt` acompanha); marcar a pasta com ONNX / LLM
- **Pré-visualização incorporada**: painel recolhível sob o navegador (Exibir → Mostrar pré-visualização, estado persistido); a seleção múltipla mostra as quatro primeiras imagens lado a lado, clique duplo em uma célula abre no visualizador flutuante; a janela flutuante tem zoom ancorado no cursor, arrastar para deslocar, clique duplo ajustar ↔ 100 %, Ctrl+0 / Ctrl+1
- **Cores e ordenação por categoria**: os dois painéis de tags recebem tons claros em 18 categorias semânticas (personagem / obra / cabelo / olhos / roupas …); o botão *Ordenar por categoria* das tags da imagem é uma alternância persistente: marcado, cada imagem recém-selecionada é agrupada por categoria automaticamente (respeitando "não ordenar as primeiras N linhas"); em Todas as tags a ordenação por categoria é opcional (desligada por padrão); as duas barras de ferramentas também trazem um menu de **filtro por categoria**: escolha uma categoria semântica (cabelo / roupas / …) para mostrar só as tags dela — somando-se à busca e ao filtro de contagem — e "Todas as categorias" restaura tudo
- **Catálogo de personagens**: ~330 mil tags de personagens do danbooru em `Data/danbooru_character_tags.csv` (incluindo ~26 mil relações pai-filho reais) para coloração exata, traduções "nome (obra)" e o agrupamento por família do corretor de tags; pode ser desativado em Configurações → Tradução

### Marcação LLM

Entrada: **Ferramentas → Marcação LLM…**, o menu de contexto do dataset, ou o botão "Gerar tags automaticamente" na barra de ferramentas de tags. Primeiro configure o endpoint compatível com OpenAI, os modelos de texto/visão e a concorrência LLM global (padrão 5, de 1 a 100) em **Configurações LLM**.

![Configurações LLM](../images/llm-settings.png)

![Marcação LLM](../images/llm-tagger.png)

- **Modo Tags** — imagem → tags, gravadas de volta no dataset conforme o modo de gravação (substituir / acrescentar / ignorar existentes), com ordenação, prefixo/sufixo e pós-processamento de sublinhados; quatro modelos de prompt integrados (Danbooru Tag / Natural Language / Mixed Mode / Natural Language 2), e os modelos personalizados são exportados como JSON sem credenciais
- **Modo Tags → Linguagem natural** (antigo TAG2NL) — tags + imagem → uma legenda em linguagem natural; formato de saída **Tags+LN / apenas LN**; salva uma cópia em `dataset_captioned/` por padrão (o `.txt` de origem permanece somente leitura; saídas existentes podem ser ignoradas) ou grava no próprio `.txt` da imagem
- **ONNX primeiro se sem tags** — imagens sem tags são primeiro marcadas pelo tagger ONNX local e depois entregues ao LLM — um pipeline automático de tags → linguagem natural

### Auditoria de tags de personagem

Entrada: **Teste → Abrir auditoria de tags…**. Defina a palavra de ativação bloqueada (sempre mantida), o estilo de marcação (**enxuto** mantém as características centrais / **completo** mantém todos os detalhes corretos), um limite mínimo de ocorrências e uma imagem de referência; a IA executa uma triagem textual seguida de uma revisão visual (não há como voltar etapas — cancele e reabra para mudar os parâmetros); por fim, revise cada decisão (manter / excluir / substituir / incerto), pré-visualize o prompt final do personagem e **Aplicar e salvar** grava de forma transacional, com reversão em caso de falha.

Há suporte a **datasets com vários personagens** (até 4): escolha o modo de sujeito Duplo ou Múltiplo e defina palavra de ativação, imagem de referência e gênero para cada personagem (linhas vazias são ignoradas, então datasets de três personagens também funcionam); as imagens são atribuídas pela palavra de ativação e depois pela pasta, imagens compartilhadas recebem automaticamente tags de contagem de sujeitos (`2girls`, `multiple girls` etc.), a revisão da IA, a revisão tag a tag e a aplicação ocorrem personagem por personagem, e um personagem que falhou pode ser repetido sozinho (os resultados dos personagens concluídos são mantidos).

![Revisão da auditoria](../images/character-tag-audit-review.png)

### Tagger ONNX

Entrada: **Ferramentas → Tagger ONNX…**, ou clique com o botão direito em **Retaguear com ONNX** nas imagens selecionadas (inicia automaticamente); o item **Marcar pasta com ONNX…** do clique direito na pasta pré-seleciona a origem *Pasta atual* e só inicia após você confirmar as configurações.

![Tagger ONNX](../images/onnx-tagger.png)

- Modelos: catálogo WD14 completo (12 modelos) + PixAI 0.9 + família CL (cl_tagger v1.02, cl_tagger_v2 v2.00 / v2.01a 🔒); limites e configurações memorizados por modelo; download do HuggingFace oficial ou do espelho
- O cl_tagger_v2 é um **repositório restrito (gated)** cuja licença do autor proíbe redistribuição e distribuição em pacotes — o aplicativo não o inclui; um aviso de licença aparece antes do download, e é preciso solicitar acesso no HuggingFace e informar o seu próprio Access Token (armazenado com criptografia DPAPI), ou colocar manualmente os arquivos baixados na pasta `Models`
- Modo de gravação (substituir / acrescentar / ignorar existentes), ordenação opcional, sublinhado→espaço, tags de prefixo/sufixo; barra de progresso para execuções em lote; o modo "Ignorando listas de tags existentes" pula imagens já marcadas antes da inferência e informa as contagens de gravadas / puladas ao concluir

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

### Localizador de imagens semelhantes

Entrada: **Ferramentas → Encontrar imagens semelhantes…**. Hash perceptual no espírito do [czkawka](https://github.com/qarmin/czkawka) (dHash + distância de Hamming), calculado direto das miniaturas em memória — milhares de imagens terminam em segundos; com uma pasta em escopo, apenas ela é varrida, e vídeos são ignorados.

- Quatro níveis de similaridade (muito alta / alta / média / baixa); resultados agrupados; **borda verde = manter, vermelha = excluir** — clique esquerdo alterna, clique direito abre o original em tamanho completo, dicas mostram nome e tamanho do arquivo, e um controle deslizante ajusta o tamanho das miniaturas
- **Manter uma por grupo (maior arquivo)** marca o restante para exclusão em um clique (heurística padrão do czkawka); cada marca ainda pode ser ajustada manualmente
- **Excluir imagens marcadas em vermelho** usa a mesma exclusão transacional da janela principal (imagem e arquivo de tags são apagados juntos, com restauração em caso de falha) e depois varre novamente de forma automática

### Corretor de tags inconsistentes

Entrada: a janela do menu **Teste** (a mesma da auditoria de tags), grupo "Correção de tags inconsistentes". Ele varre o dataset atual (ou o escopo da pasta ativa), lista cada mudança planejada como uma prévia "imagem / remover / manter / motivo" e só aplica após confirmação — como edições normais (desfazer por imagem funciona, nada é salvo automaticamente).

- **Conflitos de contagem de pessoas**: `1boy` ao lado de `2boys` remove a contagem menor (a maior por gênero sobrevive); `solo` em imagens com várias pessoas também é removido, enquanto o semanticamente diferente `solo focus` nunca é tocado
- **Duplicatas pai-filho de personagens**: quando várias tags da mesma família de personagem aparecem na mesma imagem, as contagens do dataset votam no sobrevivente; as famílias vêm das relações pai-filho reais do catálogo (variantes renomeadas como `racing miku` ↔ `hatsune miku` são pareadas, e personagens diferentes que só compartilham o nome-base nunca são mesclados)
- **Limite de confiança da tag filha** (ao lado do botão de execução; padrão 30, 0 desativa): uma variante filha com menos ocorrências no dataset do que o limite não é confiável e é incorporada ao ancestral confiável mais próximo — variantes raras e dispersas se consolidam na tag principal para um treinamento mais focado

### Linha de comando (CLI)

O próprio `BooruDatasetTagManagerPlus.exe` é uma ferramenta de linha de comando: um primeiro argumento conhecido executa sem janela (saída redirecionável; códigos de saída 0/1/2 = ok / erro / uso), qualquer outra coisa abre a interface como sempre. `help` mostra o uso completo:

- **Operações de dataset**: `stats`; consultas `list-images` / `list-tags` / `classify-tags` (filtro por tags, categoria semântica, contagem); edições em lote `add-tags` / `remove-tags` / `replace-tag` (alvo condicional, `--dry-run`); `export` para JSON
- **`fix-tags`**: o gêmeo em CLI do corretor — `--child-threshold` define o limite de confiança, `--catalog` aponta para um CSV de relações personalizado
- **`onnx-models` / `onnx-tag`**: marcação ONNX local — lista / baixa modelos automaticamente (`--hf-token` para repositórios restritos), limites e modos de gravação com a mesma semântica da interface, "ignorar existentes" filtra antes da inferência
- **`audit`**: a auditoria LLM de tags de personagem — reutiliza a configuração de API e os prompts salvos na interface, executa a revisão em duas etapas e grava de forma transacional; `--report` emite um relatório JSON, `--dry-run` só mostra as decisões
- Toda gravação é uma substituição atômica; o formato de tags (separadas por vírgula, minúsculas, sem duplicatas) é o mesmo da interface, então CLI e edições manuais se misturam livremente

### Dados e privacidade

- **A Marcação LLM e a auditoria de tags de personagem enviam imagens ao endpoint que você configurou**; a marcação ONNX, a remoção de fundo e as ferramentas de vídeo executam totalmente na sua máquina
- As configurações (incluindo as chaves de API criptografadas com DPAPI) ficam no arquivo local `settings.json`; o salvamento de tags é atômico, as ferramentas de imagem em lote gravam em arquivo temporário e só substituem em caso de sucesso, e a exclusão é feita em etapas, com restauração automática se falhar no meio. Atenção: a conversão de vídeo com "substituir o original" marcado apaga o vídeo de origem após uma conversão bem-sucedida
- O **modo de depuração** (Configurações → Geral, desligado por padrão) mostra um menu Debug e grava informações de execução e exceções no `debug.log` ao lado do executável (o menu abre o arquivo diretamente) — útil para anexar ao relatar problemas

## Agradecimentos e licença

- **[starik222](https://github.com/starik222)** — autor do [BooruDatasetTagManager](https://github.com/starik222/BooruDatasetTagManager), sobre o qual este projeto foi construído
- **[FFmpeg](https://ffmpeg.org/)** — processamento de vídeo (componente GPL incluído nos Releases)
- Licenciado sob a [Licença MIT](../../LICENSE); mantenha os avisos de copyright do upstream ao redistribuir builds modificados
