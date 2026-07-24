---
name: character-tag-auditor
description: Audit dataset-wide booru tags for a character LoRA with a locked trigger and a visual reference; supports single- and dual-character datasets.
---

# Character LoRA Tag Auditor

Return every supplied original tag exactly once with one decision: `keep`, `delete`, `replace`, or `uncertain`. Never alter the original `tag` field. Put a normalized target only in `replacement_tag`. The locked trigger is always `keep`, is included in the final prompt, and has prompt order 0.

## Categories and hard boundary

Use exactly one category: `identity`, `hair`, `eyes`, `face`, `body`, `clothing`, `footwear`, `legwear`, `wearable_accessory`, `action`, `pose`, `expression`, `scene`, `composition`, `quality`, `object`, or `other`.

Only hair, eyes, face, body appearance, clothing, footwear, legwear, and wearable accessories may be deleted or replaced. Identity/trigger tags, actions, poses, expressions, scenes/backgrounds, composition/camera terms, quality/style terms, ordinary objects, and other categories are protected: always keep them in dataset files and never replace them. Protected does not mean “include in the character prompt”; these tags normally use `include_in_prompt: false`.

## Decision meanings

- `keep`: the original tag is correct and already useful.
- `delete`: the original appearance/wearable tag is incorrect, conflicting, redundant, or non-core under the selected style.
- `replace`: the feature is correct, but the supplied tag is imprecise or redundantly split; provide one visually confirmed canonical target.
- `uncertain`: visual evidence is insufficient. This is safety-preserving and must not modify files.

A replacement target must be one tag: nonempty, different from its source, and without commas, line breaks, or control characters. Never produce a replacement chain or cycle. Several sources may map to one canonical target; the caller keeps its earliest original position.

If a replacement target equals the source because the original tag is already canonical, return `keep` with an empty `replacement_tag`. Never use `replace` merely to confirm or deduplicate an unchanged tag.

## Sparse mode (default)

Keep only the current character's core identity: subject identity/count, stable eye and hair traits, signature garments, footwear, and wearable accessories. Delete incorrect/conflicting tags and non-core appearance or wearable detail.

Prefer a visually confirmed color+item tag over generic and fragmented clothing tags. For example, `hat` plus `white headwear` can normalize to `white hat`; `skirt` or `bikini skirt` can normalize to `pink skirt`; and `hairband` plus visually verified white color can normalize to `white hairband`.

Use one canonical tag per feature family, and prefer the most specific visually confirmed tag. Do not keep a generic tag beside its more specific canonical form. When the reference confirms a bikini with a separate short skirt, delete `swimsuit`, normalize `bikini` to the confirmed colored bikini such as `pink bikini`, and normalize both `skirt` and `bikini skirt` directly to one confirmed colored skirt such as `pink skirt`. Never create a color that the visual stage did not explicitly confirm.

Apply the same specificity rule to hair structure. If `low twintails` is visually confirmed as the locked character's hairstyle, normalize `twin braids` and `twintails` directly to `low twintails`, so only `low twintails` remains. Do not apply this collapse to a tag attributed to another person or excluded from the locked character prompt.

Delete generic sources made redundant by the canonical replacement. Delete pattern, frill, ruffle, pleat, trim, fabric, and material tags in sparse mode unless indispensable to character identity. When a hat is the actual headwear, remove generic headwear/ornament descriptions that only describe decoration on the hat; keep a distinct worn accessory only when independently visible and signature.

Treat small hair and facial details as non-core in sparse mode. Delete generic or detailed bangs (`bangs`, `blunt bangs`, `crossed bangs`, and other `* bangs`), `hair between eyes`, `ahoge`, `one side up`, `fang`, `fangs`, and `mole under eye`. Also delete generic `hair ornament`, `hair accessory`, and tags whose only role is an `* hair ornament` category. These are retained when true in full mode.

Prefer one visually confirmed colored hair ribbon or colored hairband over its generic source: for example, `hair ribbon → black hair ribbon`. Delete the separate generic ornament category rather than keeping it beside the ribbon. If no colored target was explicitly confirmed for the locked character, do not invent one.

Normalize jacket tags like other garments. When a colored jacket is visually confirmed for the locked character, map `jacket`, `open jacket`, `cropped jacket`, `jacket on shoulders`, or `off shoulder jacket` directly to that one colored jacket tag. Do not infer jacket color, and do not change jacket evidence assigned to another person.

In both sparse and full mode, the final character prompt must not keep a generic garment category tag when a more specific visually confirmed tag for the same feature already exists (for example drop bare `skirt` when `black skirt` is confirmed, or bare `boots` when `black boots` is confirmed). Hair structure and other appearance details follow the LLM visual conclusion: full mode keeps real confirmed details; sparse mode may still prune non-core items per sparse rules above.

## Full mode

Keep all correct character appearance, garment, footwear, legwear, and worn-accessory details, including real plaid, frills, ruffles, pleats, trim, and materials. Delete only incorrect or conflicting details. Full mode may still replace obvious redundant generic/color fragments with one visually confirmed canonical color+item tag.

Unlike sparse mode, full mode keeps real bangs, small hair structures, fangs, moles, and specific hair ornaments when the reference confirms them.

## Hair colors

The locked character's concrete hair color tags (`white hair`, `black hair`, `pink hair`, `blonde hair`, ...) are the anchor of the hair block: when the reference confirms the color, keep the tag, and never delete it as "redundant" with a structural hair term. Never use a generic multi-color term as a replacement target for any other tag: `white hair → colored hair`, `white hair → multicolored hair`, and similar answers are always wrong — the concrete color word would disappear from the prompt, and the caller rejects such replacements.

Generic multi-color terms (`multicolored hair`, `colored hair`, `two-tone hair`, `gradient hair`, `streaked hair`, `split-color hair`, `colored inner hair`) are structure descriptions, not colors. Keep one only when the reference clearly shows two or more distinct hair colors on the locked character, and always alongside the confirmed concrete color tags (for example `multicolored hair, white hair, red hair` for white hair with red streaks). When the locked character's hair is a single color, delete these generic terms if they appear in the inventory. The specificity collapse used for hair structure (`twintails → low twintails`) never merges hair colors, and a visible hair color must survive into the final character prompt.

## Multiple people, multi-character datasets, and hair colors

Attribute appearance to the locked character using the reference. A nearby shade on the same character may be normalized only when it describes the same visible feature. A clearly different hair color may belong to another character in a multi-person image: keep it in dataset files, but set `include_in_prompt: false` so an other character trait does not contaminate the locked character prompt. Do not delete protected subject-count tags such as `2girls` or `multiple girls`; the caller injects or corrects subject counts (`2girls`, `multiple girls`, `1girl` + `1boy`) deterministically on shared images, so never invent them yourself.

A merged training set may contain several characters, one repeat folder each, and the caller may audit two characters in one session. Each audit call still locks exactly one character: the supplied trigger word and reference image define who is being audited, and the inventory only comes from that character's member images. When another audited character's trigger word or traits appear in the inventory (from images containing both characters), treat them as other-person evidence: `keep`, `include_in_prompt: false`, never delete, never replace, and never merge the two characters' features. A shared garment word such as `skirt` or `hat` must be resolved only from the locked character's own appearance in the reference; the caller reconciles conflicting per-character replacements per image, so answer for the locked character only.

## Two-stage review

Text screening uses meaning and frequency for a preliminary decision. Frequency is evidence, not proof. Visual review must re-check colors, garment identity, headwear, hair style, and character ownership against the reference image. If a plausible feature is occluded or out of frame, prefer `uncertain` over guessing.

Visual review must explicitly list and re-check every color-less garment, footwear, legwear, and wearable accessory tag it receives (for example `jacket`, `boots`, `shirt`, `skirt`, `hair ribbon`, `hairband`), including tags the text stage marked `keep`. When the reference clearly shows that item's color on the locked character, answer `replace` with the color-prefixed tag — for example `jacket → black jacket` or `boots → black boots` — even if that colored tag does not currently exist anywhere in the tag inventory. Keep the color-less tag only when its color is genuinely unverifiable in the reference (occluded, out of frame, or ambiguous), and state that in `reason`. Never answer `replace` with an empty `replacement_tag`; a replace decision without a target is invalid.

## Canonical prompt order

Assign `prompt_order` so the final sparse prompt reads as a stable visual-weight pyramid:

1. locked trigger (always order 0)
2. subject count (`1girl`) and `solo` when true
3. eye color
4. hair color → hair length → hair structure (`low twintails`, `short hair`, `low ponytail`)
5. hair-worn accessories (`hair flower`, `hair ribbon`, `white hairband`, `hair ornament` when kept)
6. headwear (`white beret`, `black hat`, `halo`, `sleep mask`)
7. face/ear/neck jewelry (`earrings`, `white choker`, `black necklace`, generic `jewelry` when kept)
8. upper-body clothing (`white cropped shirt`, `white jacket`, dress or leotard)
9. swimwear/one-piece layers (`pink bikini`), then lower-body clothing (`pink skirt`, `blue denim shorts`)
10. arm/hand wear (`black gloves`, `bracelet`, `bandolier`)
11. legwear (`white thighhighs`, `white pantyhose`, `thigh strap`)
12. footwear (`white slippers`, `high heels`, `brown thigh boots`)

Keep one canonical tag per feature slot; a slot that is not visually confirmed is simply absent. Never pad the prompt with quality, pose, scene, or composition tags.

## Reference sparse prompts

These verified single-character prompts are the target shape and density for sparse mode (trigger first, then the pyramid above):

- `lynae (peppermint) (wuthering waves), 1girl, solo, purple eyes, blonde hair, very long hair, low twintails, hair flower, earrings, white choker, black necklace, white cropped shirt, blue bikini, blue denim shorts, bracelet, white thigh strap`
- `chisa (peach parfait) (wuthering waves), 1girl, solo, black hair, purple eyes, very long hair, low twintails, white beret, white hairband, hair flower, earrings, pink bikini, pink skirt, white bracelet, pink thigh strap`
- `citlali \(whispers of stars and smoke\) \(genshin impact\), 1girl, blue eyes, pink hair, purple hairclip, low twintails, sleep mask, purple dress, white slippers`
- `exusiai the new covenant \(the legend seeker\) \(arknights\), 1girl, orange eyes, red hair, halo, black hat, black jacket, red cape, black dress, black gloves, brown thigh boots`
- `alf \(silver palace\), 1girl, blue eyes, pink hair, hair ribbon, cable tail, white maid apron, black skirt, red bowtie, bandolier, thigh strap, white pantyhose, high heels`
- `velina airgid, 1girl, purple eyes, white hair, long hair, hair bow, earrings, blue dress, jewelry, black gloves, white thighhighs`
- `zhuang fangyi \(arknights\), 1girl, green eyes, black hair, hair ornament, green dress, jewelry, black gloves`
- `mi fu \(arknights\), 1girl, pink hair, white jacket, red shirt, black shorts, red gloves, green thighhighs`
- `promeia \(zenless zone zero\), 1girl, purple eyes, purple hair, short hair, low ponytail, black leotard, black cape, jewelry, black thighhighs, thigh boots`
- `sigrika \(wuthering waves\), 1girl, orange hair, long hair, white dress, black shorts, white gloves`

Match their density: a sparse prompt is typically 8–16 tags. Note `denia haonvhai \(wuthering waves\)` and `denia huainvhai \(wuthering waves\)` are two different forms of one character with separate triggers — in a merged dataset each form is audited as its own locked character, exactly like two distinct characters.

## Chisa regression example

For `chisa (peach parfait) (wuthering waves)` in the supplied swimsuit reference, the intended sparse character prompt is:

`chisa (peach parfait) (wuthering waves), 1girl, red eyes, black hair, long hair, low twintails, white hat, white hairband, earrings, pink bikini, pink skirt, bracelet`

The dataset can still keep protected tags such as `looking at viewer`, `outdoors`, `smile`, `sky`, `ocean`, and other-person evidence such as `2girls`, `multiple girls`, and `blonde hair`; those do not enter the locked character prompt. Redundant sources such as `swimsuit`, `bikini`, `hat`, `white headwear`, `skirt`, `bikini skirt`, `twin braids`, `twintails`, `plaid`, `plaid bikini`, `frills`, and `frilled bikini` should be normalized or deleted according to sparse rules and visual evidence.

## Output safety

- Return strict JSON in the caller's schema, with no prose or markdown fence.
- Cover every input tag exactly once; never add an original tag that was not supplied.
- Explain each decision briefly and concretely.
- Only visually confirmed core character traits use `include_in_prompt: true`.
- Never estimate missing facts. Use `uncertain`.
