---
name: prompt-pyramid
description: Reorder and purify scattered image-generation tags (Stable Diffusion, Flux, NovelAI, ComfyUI, WebUI, danbooru-style booru tags) into a single clean comma-separated prompt string built on a visual-weight pyramid. Use this skill PROACTIVELY whenever the user is writing or fixing a prompt for diffusion models — tags scattered across a list, an unordered tag dump, a character sheet needing a prompt, "help me organize these tags", "clean up this prompt", "why is the color bleeding onto the skin", "extra limbs / floating accessories", or any mention of 1girl/1boy, trigger words, LoRA character prompts, booru tags, danbooru tags, or ComfyUI/WEBUI positive prompt boxes. Also use it when the user pastes a raw tag list and wants it production-ready. Do not use it for natural-language conversational prompts (e.g. Midjourney-style sentences) — this skill is for comma-separated booru-style tags only.
---

# Prompt Pyramid — Visual-Weight Reordering for Diffusion Tags

## What this skill does

Take a messy, unordered pile of booru-style tags and return **one clean comma-separated string**, ordered so the model's attention mechanism locks onto identity and color first, then sculpts the head, then dresses the body outward-to-inward and top-to-bottom, and finally anchors the feet. The goal is: the model never has to "look back" to revise a region it already rendered, which is how color bleeding, floating accessories, and extra limbs happen.

Output format: **a single line of comma-separated tags, lowercase, spaces instead of underscores, no parenthetical weights, no artist strings, no duplicates.** Nothing else in the final output line unless the user explicitly asks for explanation.

## Why ordering matters (the core mental model)

Diffusion text encoders assign higher attention weight to tokens that appear earlier in the prompt. This is not a myth — it's the same positional bias you see in any transformer over a sequence. Two practical consequences:

1. **Whatever comes first gets rendered first and most "solidly."** Later tags are interpreted as modifications to an already-formed region. So if a hair accessory comes *before* the hair color, the model often paints the accessory color onto the hair, or draws hair that doesn't match the stated color.
2. **The model hates revising.** If head features are split across the prompt (one hair tag at the top, another at the bottom), attention to "the head" flickers on and off, and you get the classic failure modes: duplicate hair accessories, a stray arm, a floating ribbon. Grouping all of one region's tags together lets the model render that region once, confidently, and move on.

So the ordering below isn't aesthetic — it's a sequence that minimizes revision.

## The six-tier pyramid

Read this top to bottom. That is the order tags must appear in the final string.

### Tier 1 — Identity (the skeleton)
`1girl` / `1boy` / `0girl` / `multiple girls` / `solo` / `crowd` · character count · **the LoRA/character trigger word**

This is the load-bearing wall. If the trigger word or subject count is wrong, every downstream tag (clothes, accessories) becomes "no one's property" and the model improvises — usually badly. The trigger word especially must sit at the very front, because LoRA conditioning is most stable when the trigger is the first thing the encoder sees.

### Tier 2 — Dominant color (the face anchor)
`eye color` · `hair color`

Eyes and hair occupy ~80% of the face's visual focus. Pinning these two immediately after identity lets the model "sketch the upper body" before any detail arrives. Getting color down early also prevents later clothing colors from contaminating the hair/skin, because the hair color is already committed.

### Tier 3 — Sculpting the head (local detail)
`hair length/style` (long hair, short hair, twin drills…) · `hair features` (ahoge, sidelocks, hair between eyes, blunt bangs) · `head & neck accessories` (hair ribbon, hairclip, hairband, earrings, necklace, glasses)

All head-region tags get bundled here, immediately after the hair color that governs them. This is the "render the head once, then leave it alone" rule. Do not let a hair feature tag wander off to the end of the prompt.

### Tier 4 — Torso (outer → inner, by occlusion)
`outerwear` → `midlayer/capes` → `inner garment`

Order by what covers what. The visually outermost garment comes first so the model treats inner layers as "peeking through" rather than as the dominant color. Example: `jacket` → `cape` → `dress`. If you flip this, the inner dress color tends to bleed onto the jacket.

### Tier 5 — Limbs (top → bottom, following the body)
`gloves / arm wear / hand accessories (handbag)` → `legwear (thighhighs, pantyhose, garter straps)`

Follows natural body flow from torso outward and downward. Hand items before leg items.

### Tier 6 — Feet (the visual anchor, always last)
`shoes / boots / footwear`

Footwear is pinned to the very end for two reasons: it matches the human head-to-toe reading order, and more importantly it **anchors the lower body** — the model has somewhere stable to "land" the composition, which prevents leg warping and floating-feet artifacts. `black boots` should be the last tag of the character block.

### Optional — After the character block
`background / setting` · `quality / aesthetic boosters` (masterpiece, best quality, highly detailed)

Background and quality tags sit *after* the entire character, because they describe a different subject (the scene, the render quality) and shouldn't compete for attention while the character is being built. If the user didn't supply any, don't invent them — leave the string as-is. Do not prepend `masterpiece, best quality` unless the user included those.

## Normalization & purification rules

Before ordering, clean the input. The point is to leave only pure, high-frequency feature tokens so the base model and LoRA have maximum freedom — anything redundant or artist-flavored fights the LoRA and dirties the style.

1. **Underscores → spaces.** `long_hair` becomes `long hair`. Comma-separated booru tags use spaces.
2. **Strip artist and character-name strings.** Remove `by <artist>`, `artist:xxx`, `source:xxx`, and any non-trigger character-name tags. These bias the output toward a specific artist's style and pollute the LoRA's intended look. Keep only the user's stated trigger word.
3. **Strip redundant beauty/face descriptors.** Tags like `beautiful, cute, gorgeous, detailed eyes, perfect face` are vague quality fluff that compete with the LoRA. Remove them unless the user specifically insists on one. Keep concrete features (`heterochromia`, `sharp eyes`, `smile`) — those are structural, not fluff.
4. **Deduplicate.** If `long hair` appears three times, keep one. Near-duplicates (`black thighhighs` + `black thighhighs`) collapse to one.
5. **No parenthetical weights.** This skill does not emit `(word:1.3)`. If the user supplied weights, strip the parentheses and the number, keep the word.
6. **Lowercase everything.** Booru tags are lowercase.
7. **Preserve every concrete feature the user gave.** Purification removes fluff, not features. If unsure whether a tag is fluff or feature, keep it — it's worse to drop a real feature than to leave one extra tag.

## The pipeline you follow every time

1. **Read** the user's input (a tag list, a character sheet, a broken prompt).
2. **Normalize**: apply rules 1–6 above. Don't drop features yet.
3. **Classify** every remaining tag into one of the six tiers (or the optional background/quality bucket). If a tag doesn't fit any tier (e.g. a pose like `sitting`, `outstretched hand`), put it at the very end of the character block, before background — poses are body-level but not region-specific.
4. **Order** within each tier:
   - Tier 3: hair length/style → hair features → head accessories.
   - Tier 4: strictly outer → inner.
   - Tier 5: hands → legs.
   - Tier 6: footwear last.
5. **Deduplicate** across the whole string one more pass.
6. **Emit** a single comma-separated line. One space after each comma, no trailing space, no trailing period.

If the user gave a tag you genuinely cannot classify, put it at the end of the character block (before background) and move on — don't agonize, don't ask, just place it sensibly.

## Worked examples

### Example 1 — scattered character sheet

**Input (as given):**
```
artist:greg rutkowski, long_hair, black_boots, 1girl, blue_eyes, ahoge, red_jacket, white_dress, blonde_hair, sidelocks, hair_ribbon, black_gloves, black_thighhighs, cute, beautiful, solo, hair_between_eyes
```

**Internal normalization:** strip `artist:greg rutkowski`, `cute`, `beautiful`. Convert underscores to spaces. Lowercase.

**Final output:**
```
1girl, solo, blonde hair, blue eyes, long hair, ahoge, sidelocks, hair between eyes, hair ribbon, red jacket, white dress, black gloves, black thighhighs, black boots
```

Notice the tier flow: `1girl, solo` (T1) → `blonde hair, blue eyes` (T2) → `long hair, ahoge, sidelocks, hair between eyes, hair ribbon` (T3, hair length → hair features → head accessory) → `red jacket, white dress` (T4, outer → inner) → `black gloves, black thighhighs` (T5, hands → legs) → `black boots` (T6, anchor).

### Example 2 — outfit with a midlayer

**Input:**
```
green_cape, 1girl, brown_eyes, twin_drills, pink_hair, hairband, white_blouse, long_skirt, ankle_boots, white_gloves, sitting
```

**Final output:**
```
1girl, pink hair, brown eyes, twin drills, hairband, green cape, white blouse, long skirt, white gloves, ankle boots, sitting
```

`green cape` (outer) before `white blouse` (inner) before `long skirt` (lower-torso garment, treated as continuation of torso block). `sitting` is a pose — non-region, goes at the end of the character block.

### Example 3 — multiple subjects

**Input:**
```
2girls, blue_dress, red_dress, blonde_hair, black_hair, blue_eyes, sitting, holding_hands
```

**Final output:**
```
2girls, blonde hair, black hair, blue eyes, blue dress, red dress, sitting, holding hands
```

When features belong to two different characters, keep each feature-type grouped (all hair together, all dresses together) rather than trying to attribute per-character — the encoder handles "two of each" more reliably than interleaved attribution.

## Edge cases & judgment calls

- **Pose and action tags** (`sitting`, `standing`, `outstretched hand`, `looking at viewer`, `holding hands`): these are body-level but not region-specific. Place them at the end of the character block, before any background tags.
- **Facial expression** (`smile`, `open mouth`, `blush`): goes in Tier 3 with the head, after hair features — the face is part of the head region.
- **Background tags** (`outdoors`, `classroom`, `night`, `sky`): optional bucket, after the entire character block. Only include if the user supplied them.
- **Quality boosters** (`masterpiece`, `best quality`, `highres`): optional bucket, dead last. Only if the user included them — do not add your own.
- **A tag you can't classify**: end of character block, before background. Don't stall on it.
- **Conflicting tags** (`long hair` and `short hair` both present): keep whichever the user emphasized more; if truly ambiguous, drop the weaker one. Contradictory tags cause the revision problem this whole skill exists to prevent.
- **The user wants an explanation, not just the string**: by default output only the string. If they asked "why" or "explain", walk them through the tiers you used. The string still comes first.

## What NOT to do

- Don't add weights like `(blue eyes:1.2)` — out of scope for this skill.
- Don't generate a negative prompt — out of scope.
- Don't insert `<lora:...>` blocks — out of scope. If the user gave a trigger word, keep it at Tier 1; the LoRA block itself is their job to place.
- Don't invent unsupported features. In character-audit mode, you may combine supplied attributes into one canonical tag only when the component facts were supplied and visually confirmed (for example, `white` + `hat` to `white hat`).
- Don't output a structured breakdown unless asked — the default deliverable is one clean line.

## Character-audit mode

When this skill is used by the character tag auditor, emit only the locked character's core prompt. The locked trigger is always first, followed by subject identity/count, eye and hair colors, hair structure, head accessories, clothing from torso downward, worn accessories, and footwear. Respect the auditor's `include_in_prompt` and `prompt_order` fields.

Use one canonical tag per feature family. When several visually confirmed tags describe the same feature, keep only the most specific canonical form: for example, prefer `low twintails` over `twintails` and `twin braids`, `pink bikini` over `bikini` or `swimsuit`, and one `pink skirt` over `skirt` plus `bikini skirt`. Sources may map directly to the same canonical target, but never form a replacement chain.

An already canonical source stays `keep`; never emit a replacement whose target is identical to its source.

For a sparse character audit, omit non-core bangs and small hair/face details such as `hair between eyes`, `ahoge`, `one side up`, `fangs`, and `mole under eye`, and omit generic `hair ornament` categories. Prefer one visually confirmed colored hair ribbon or colored hairband over the generic accessory tag. Likewise, collapse generic jacket and jacket-style aliases to one visually confirmed colored jacket. Never invent either color, and never borrow a target from another character. Full audits may keep these real details.

Prefer color-prefixed garment, footwear, and accessory tags in the final character prompt whenever the reference visually confirmed the color, even when the colored form never appeared in the original tag list — for example emit `black jacket` and `black boots` instead of bare `jacket` and `boots`. A color-less garment tag may stay in the prompt only when its color was genuinely unverifiable in the reference.

In both sparse and full character audits, never emit a generic garment category tag alongside a more specific confirmed tag for the same feature. Hair and structural appearance tags follow the auditor's LLM decisions; full mode keeps confirmed real details.

Do not automatically include protected scene, action, pose, expression, composition, quality, ordinary-object, or other-character traits. They may remain unchanged in dataset files while being absent from the final character prompt. A canonical combined tag is allowed only from existing, visually confirmed attributes; never create a color, garment, body trait, or accessory that was not supported by the input and reference image.
