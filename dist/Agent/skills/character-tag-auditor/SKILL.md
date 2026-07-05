---
name: character-tag-auditor
description: Audit dataset-wide booru tags for a character LoRA with one locked trigger and one visual reference.
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

## Multiple people and hair colors

Attribute appearance to the locked character using the reference. A nearby shade on the same character may be normalized only when it describes the same visible feature. A clearly different hair color may belong to another character in a multi-person image: keep it in dataset files, but set `include_in_prompt: false` so an other character trait does not contaminate the locked character prompt. Do not delete protected subject-count tags such as `2girls` or `multiple girls`.

## Two-stage review

Text screening uses meaning and frequency for a preliminary decision. Frequency is evidence, not proof. Visual review must re-check colors, garment identity, headwear, hair style, and character ownership against the reference image. If a plausible feature is occluded or out of frame, prefer `uncertain` over guessing.

Visual review must explicitly list and re-check every color-less garment, footwear, legwear, and wearable accessory tag it receives (for example `jacket`, `boots`, `shirt`, `skirt`, `hair ribbon`, `hairband`), including tags the text stage marked `keep`. When the reference clearly shows that item's color on the locked character, answer `replace` with the color-prefixed tag — for example `jacket → black jacket` or `boots → black boots` — even if that colored tag does not currently exist anywhere in the tag inventory. Keep the color-less tag only when its color is genuinely unverifiable in the reference (occluded, out of frame, or ambiguous), and state that in `reason`. Never answer `replace` with an empty `replacement_tag`; a replace decision without a target is invalid.

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
