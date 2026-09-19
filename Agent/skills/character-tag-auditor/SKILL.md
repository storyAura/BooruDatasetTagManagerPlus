---
name: character-tag-auditor
description: Audit dataset-wide booru tags for a character LoRA with a locked trigger and a visual reference; supports single-, dual- and multi-character datasets of up to four characters.
---

# Character LoRA Tag Auditor

Return every supplied original tag exactly once with one decision: `keep`, `delete`, `replace`, or `uncertain`. Never alter the original `tag` field; a normalized target goes only in `replacement_tag`. The locked trigger is always `keep`, `include_in_prompt: true`, `prompt_order: 0`.

Decisions: `keep` = correct and already canonical · `delete` = wrong, conflicting, redundant, or non-core under the style · `replace` = the feature is real but the tag is imprecise or split; give one visually confirmed canonical target · `uncertain` = evidence insufficient (safe; never modifies files). A replacement target is one tag: nonempty, different from its source, no commas or line breaks, never a chain or cycle. Several sources may map to one target. If a replacement target equals the source because the tag is already canonical, answer `keep` with an empty `replacement_tag`; never use `replace` to confirm an unchanged tag. Never answer `replace` with an empty `replacement_tag`.

## Procedure — run these steps, in order, for every tag

**Step 1 — Category and protection.** Use exactly one category: `identity`, `hair`, `eyes`, `face`, `body`, `clothing`, `footwear`, `legwear`, `wearable_accessory`, `action`, `pose`, `expression`, `scene`, `composition`, `quality`, `object`, `other`. Only hair, eyes, face, body, clothing, footwear, legwear, and wearable accessories may be deleted or replaced. Everything else is protected: `keep`, never replace, normally `include_in_prompt: false` (protected does not mean "in the character prompt").

**Step 2 — Attribution.** Decide from the reference image, not from frequency, whether the tag describes the locked character. A trait of any other character in the images (another audited character's trigger or features, a clearly different hair color in a multi-person image) is other-person evidence: `keep`, `include_in_prompt: false`, reason names that character, never delete, replace, or merge it with the locked character. Never delete subject-count tags (`2girls`, `multiple girls`, ...) and never invent them; the caller injects counts on shared images. A shared garment word such as `skirt` or `hat` is resolved only from the locked character's own appearance. Two forms of one character with separate triggers (`denia haonvhai` / `denia huainvhai`) are audited as two locked characters.

**Step 3 — Existence.** For appearance/wearable tags: not present on the locked character in the reference → `delete`; plausible but occluded or out of frame → `uncertain`. Text screening may use meaning and frequency for a preliminary decision, but frequency is evidence, not proof; visual review re-checks colors, garment identity, headwear, hair style, and ownership against the image.

**Step 4 — Color binding (mandatory for wearables).** Visual review must explicitly list and re-check every color-less garment, footwear, legwear, and wearable accessory tag it receives (`jacket`, `boots`, `shirt`, `skirt`, `choker`, `hair ribbon`, `hairband`, `bow`, ...), including tags the text stage marked `keep`. When the reference shows the item's color on the locked character, answer `replace` with the color-prefixed tag — `jacket → black jacket`, `boots → black boots` — even if that colored tag does not currently exist anywhere in the tag inventory. A color-less wearable may stay `keep` only when its color is genuinely unverifiable; then the reason must start with `color unverifiable:` and say why (occluded, out of frame, ambiguous). Any other `keep` of a color-less wearable is wrong. Never invent a color the image does not show.

**Step 5 — Container tags and same-item pairs (both modes).** Booru tags form trees: `earrings` implies `jewelry`; `elbow gloves` and `black gloves` imply `gloves`; `hair ribbon` and `black ribbon` imply `ribbon`; `frilled dress` implies `dress` and `frills`. Use one canonical tag per feature family and never keep a container beside a confirmed member of its family:
- **Known tags win.** A replacement target must be a real booru tag or `color` + a real tag (`black hairband`, `black hair ribbon`, `blue earrings`). Never invent multi-modifier composites: `frilled black dress`, `black frilled dress`, `black elbow gloves`, `black striped thighhighs` are not tags. When the inventory already has a precise colored tag (`black dress`, `black gloves`, `black ribbon`) that is the canonical tag for that item — never replace it. Fold or delete the decoration/type word: `frills` next to `black dress` → `frills` delete (full mode too; reason: decoration of the black dress); `elbow gloves` next to `black gloves` → `elbow gloves` replace → `black gloves`; `hair ribbon` next to `black ribbon` → `hair ribbon` replace → `black ribbon`. Color-bind a type word (`elbow gloves → black elbow gloves`) only when no precise colored tag exists — allowed because it is `color` + a real tag.
- **One item, one tag.** One accessory keeps one tag. When `hairband` and `hair ribbon` name the same headpiece, pick one from the reference (a band → `black hairband`; a tied ribbon → `black hair ribbon`) and replace the other with it; never keep two colored tags for one headpiece. Same for `bow` / `hair bow` / `ribbon`.
- Containers (`jewelry`, `bow`, `ribbon`, `hair ornament`, `hair accessory`, `headwear`, `gloves`, `dress`, `skirt`, `legwear`): `jewelry` + `earrings` → `jewelry` is `replace` → `earrings`; with two members (`earrings` + `necklace`) `delete` the container. A container beside exactly one confirmed member is `replace` → that member (`dress → black dress`, `gloves → black gloves`), never `delete` — deleting only strips the word, replacing propagates the precise tag to every image. Bare `jewelry` survives only when no specific piece was confirmed. `hat` + `white headwear` → `white hat`; `hair ornament` beside any specific hair accessory, `headwear` beside a hat, `hairband` beside a colored hairband → collapse to the specific tag.
- Same-item pairs: when two confirmed tags describe ONE item, one giving the color and the other the type (`black gloves` + `elbow gloves`, `hair ribbon` + `black ribbon`, `black thighhighs` + `striped thighhighs`), replace the type tag with the precise colored tag that already exists. If the reference cannot confirm they are the same item, keep the color tag and mark the type tag `uncertain`. Never emit the pair as two tags.
- `bow` / `ribbon` naming the same hair accessory as a confirmed `hair ribbon` / `hair bow` → `replace` with that specific tag; keep a bare `bow` only when a second, independently visible bow exists elsewhere, and say where.
- Decoration words (`frills`, `ruffles`, `pleated`, `lace trim`) attach only when the garment itself is that decorated form and no more precise colored tag exists (`frilled dress`); otherwise delete them. Never leave a bare decoration word in the prompt.
- Hair structure follows the same specificity rule: when `low twintails` is confirmed, `twin braids` and `twintails` → `low twintails`. Colored jacket confirmed → `jacket`, `open jacket`, `cropped jacket`, `jacket on shoulders`, `off shoulder jacket` → that one colored jacket (never infer jacket color). `skirt` / `bikini skirt` next to a confirmed `pink skirt` → `pink skirt`; bikini + separate skirt confirmed → delete `swimsuit`, `bikini` → the confirmed colored bikini.
- The caller re-applies these collapses deterministically from the danbooru implication table after your answer and only keeps a combined tag that exists in that vocabulary (otherwise it keeps the color tag). Do not rely on it: give the collapse yourself so `reason` explains it and dataset files are rewritten consistently.

**Step 6 — Style pruning.**
- Sparse (default): keep only core identity — subject count, stable eye and hair traits, signature garments, footwear, wearable accessories. Delete small hair and facial details: `bangs` and every `* bangs`, `hair between eyes`, `ahoge`, `one side up`, `fang`, `fangs`, `mole under eye`; generic `hair ornament` / `hair accessory` and tags whose only role is an `* hair ornament` category; pattern, frill, ruffle, pleat, trim, fabric, and material tags unless indispensable to identity; decoration that only describes the hat. Prefer one visually confirmed colored hair ribbon or colored hairband (`hair ribbon → black hair ribbon`) over the generic source, and one colored jacket over jacket aliases.
- Full: keep every correct detail, including real plaid, frills, ruffles, pleats, trim, materials, bangs, fangs, moles, and specific hair ornaments; delete only incorrect or conflicting details. Full mode still runs Steps 4 and 5 exactly like sparse — real detail never means a container word beside its member or two tags for one garment.

**Step 7 — Hair colors (never merge).** Concrete hair color tags (`white hair`, `pink hair`, `blonde hair`, ...) anchor the hair block: when the reference confirms the color, keep it and never delete it as redundant with a structure term. Never use a generic multi-color term as a replacement target: `white hair → colored hair` or `→ multicolored hair` is always wrong and is rejected by the caller. `multicolored hair`, `colored hair`, `two-tone hair`, `gradient hair`, `streaked hair`, `split-color hair`, `colored inner hair` are structure words: keep one only when two or more distinct hair colors are visible on the locked character, always alongside the concrete colors; delete them when the hair is a single color. A visible hair color must survive into the final prompt.

**Step 8 — Canonical prompt order and inclusion.** Only visually confirmed core traits of the locked character use `include_in_prompt: true`. Assign `prompt_order` as a visual-weight pyramid: 1 trigger (0) · 2 subject count / `solo` · 3 eye color · 4 hair color → length → structure · 5 hair-worn accessories · 6 headwear · 7 face/ear/neck jewelry (`earrings`, `white choker`; bare `jewelry` only when no specific piece exists) · 8 upper-body clothing · 9 swimwear / one-piece, then lower-body clothing · 10 arm/hand wear · 11 legwear · 12 footwear. One canonical tag per slot; an unconfirmed slot is absent. Never pad the prompt with quality, pose, scene, or composition tags.

## Reason format

`reason` must state the evidence you used: what is visible in the reference (`black ribbon tied behind the white hairband`, `skirt hidden by the cape`), which container/member or pair rule applied, or which other character the trait belongs to. Stock phrases such as `core tag`, `required tag`, `standard tag` are not reasons and are treated as no review.

## Reference sparse prompts

These verified single-character prompts are the target shape and density for sparse mode (trigger first, then the pyramid above):

- `lynae (peppermint) (wuthering waves), 1girl, solo, purple eyes, blonde hair, very long hair, low twintails, hair flower, earrings, white choker, black necklace, white cropped shirt, blue bikini, blue denim shorts, bracelet, white thigh strap`
- `chisa (peach parfait) (wuthering waves), 1girl, solo, black hair, purple eyes, very long hair, low twintails, white beret, white hairband, hair flower, earrings, pink bikini, pink skirt, white bracelet, pink thigh strap`
- `citlali \(whispers of stars and smoke\) \(genshin impact\), 1girl, blue eyes, pink hair, purple hairclip, low twintails, sleep mask, purple dress, white slippers`
- `exusiai the new covenant \(the legend seeker\) \(arknights\), 1girl, orange eyes, red hair, halo, black hat, black jacket, red cape, black dress, black gloves, brown thigh boots`
- `alf \(silver palace\), 1girl, blue eyes, pink hair, hair ribbon, cable tail, white maid apron, black skirt, red bowtie, bandolier, thigh strap, white pantyhose, high heels`
- `velina airgid, 1girl, purple eyes, white hair, long hair, hair bow, earrings, blue dress, black gloves, white thighhighs`
- `zhuang fangyi \(arknights\), 1girl, green eyes, black hair, hair ornament, green dress, jewelry, black gloves`
- `mi fu \(arknights\), 1girl, pink hair, white jacket, red shirt, black shorts, red gloves, green thighhighs`
- `promeia \(zenless zone zero\), 1girl, purple eyes, purple hair, short hair, low ponytail, black leotard, black cape, jewelry, black thighhighs, thigh boots`
- `sigrika \(wuthering waves\), 1girl, orange hair, long hair, white dress, black shorts, white gloves`

Match their density: a sparse prompt is typically 8–16 tags.

## Chisa regression example

For `chisa (peach parfait) (wuthering waves)` in the supplied swimsuit reference, the intended sparse character prompt is:

`chisa (peach parfait) (wuthering waves), 1girl, red eyes, black hair, long hair, low twintails, white hat, white hairband, earrings, pink bikini, pink skirt, bracelet`

The dataset still keeps protected tags such as `looking at viewer`, `outdoors`, `smile`, `sky`, `ocean`, and other-person evidence such as `2girls`, `multiple girls`, `blonde hair`; none enter the locked character prompt. Redundant sources `swimsuit`, `bikini`, `hat`, `white headwear`, `skirt`, `bikini skirt`, `twin braids`, `twintails`, `plaid`, `plaid bikini`, `frills`, `frilled bikini` are normalized or deleted by Steps 5–6 and the visual evidence.

## Output safety

- Return strict JSON in the caller's schema, no prose, no markdown fence.
- Cover every input tag exactly once; never add a tag that was not supplied.
- Never estimate missing facts — use `uncertain`.
