# Prompts for generating character and animal images (copy and paste)

The prompts are in English because image tools understand English best.
How to use them: create the images yourself, then send them to Claude Code. Claude Code will clean them, cut them to exact pixels, line up the feet, assemble the sprite sheets and wire them into the game.

## 0. Important before you start
- **Make the "style reference" (step 1) first, once.** When creating every other character, attach that reference image and use the same tool, model and settings, so they all look like the same game.
- **The background must be flat magenta `#FF00FF`.** Image tools often fake transparency as a checkerboard; a flat colour is easy for Claude Code to remove.
- **Side view facing right only.** Each character faces the same way in every frame; the game mirrors it to face left.
- If the tool can't produce a full animation sheet that looks good, **one or two poses per character are enough** (idle + walk). Claude Code will build the remaining frames from them.
- Don't name an existing game or artist in the prompt. This project must not copy another game's style or IP.
- Keep the original files plus a note of the tool and settings used (for the asset credits).

### Sizes that fit the game (fixed)
| Character | Final size per frame | Approximate body height |
|---|---|---|
| Human / demon | 32 × 32 px | 26–28 px |
| Sheep, deer, wolf | 32 × 32 px | — |
| Rabbit | 32 × 32 px frame (body about 14 px) | — |
| Dragon | 64 × 64 px | — |

Image tools output large images (e.g. 1024 px). That's fine **as long as the pixels are big, square and evenly sized**: a 32 px sprite enlarged 16 times.

---

## 1. Style reference (do this first)

```
Original pixel art character sprite style sheet for a cozy fantasy world simulation game.
Warm, hand-crafted pixel art with clean 1-pixel dark outlines, limited palette (about 24 colors),
soft cel shading lit from the top-left, golden warm tones, small chibi proportions (big head, small body).
Show 4 sample sprites side by side, each a full body, side view facing right:
a human farmer, a demon villager with small horns and a thin tail, a sheep, a green dragon.
Each sprite is 32x32 pixels, rendered as crisp pixel art scaled up 16x with nearest-neighbor
(every pixel a perfect square, no blur, no anti-aliasing, no gradients inside pixels).
Flat solid magenta background #FF00FF, no ground, no shadows on the background, no text, no frame.
```

---

## 2. Template for each character (replace `<CHARACTER>` and `<DETAILS>`)

**Standard version (one sheet):**
```
Pixel art sprite animation sheet of <CHARACTER>, original design, same style as the attached reference.
<DETAILS>
Side view, facing right, full body, feet on the same baseline in every frame.
One row of frames, evenly spaced on an invisible grid, left to right:
idle 1, idle 2 (breathing), walk 1, walk 2, walk 3, walk 4, work 1, work 2, attack 1 (wind-up), attack 2 (strike),
sit and read 1, sit and read 2, lying down defeated.
Each frame is a 32x32 pixel sprite scaled up 16x with nearest-neighbor: perfectly square pixels, no blur,
no anti-aliasing, 1-pixel dark outline, limited warm palette, soft cel shading from the top-left.
Flat solid magenta background #FF00FF. No text, no labels, no grid lines, no shadows on the background.
```

**Short version (if the sheet comes out messy, make 2 separate images instead):**
```
Single pixel art sprite of <CHARACTER>, original design, same style as the attached reference.
<DETAILS>
<POSE: standing idle | mid-walk step with one leg forward>, side view facing right, full body.
32x32 pixel sprite scaled up 16x with nearest-neighbor, perfectly square pixels, no anti-aliasing,
1-pixel dark outline, limited warm palette. Flat solid magenta background #FF00FF, nothing else in the image.
```

---

## 3. `<CHARACTER>` / `<DETAILS>` values for each character

### Humans (aim for at least 4 variants; the game picks one per person)
| # | `<CHARACTER>` | `<DETAILS>` |
|---|---|---|
| H1 | a young human farmer | straw hat, short brown hair, blue linen tunic, brown trousers, leather boots, holds a wooden hoe in the work frames and a short sword in the attack frames |
| H2 | a human woman villager | long auburn hair tied back, green dress with apron, sandals, holds a hoe in the work frames and a short sword in the attack frames |
| H3 | an older human villager | grey beard, bald head, purple tunic, rope belt, holds a hoe in the work frames and a short sword in the attack frames |
| H4 | a human scholar | short dark hair, teal robe, small satchel, holds an open book in the reading frames and a short sword in the attack frames |
| H5 (optional) | a human town guard | simple iron helmet, padded red gambeson, spear in the attack frames |

### Demons (people who can farm and trade like humans; not evil by nature)
| # | `<CHARACTER>` | `<DETAILS>` |
|---|---|---|
| D1 | a demon villager | crimson skin, two small curved ivory horns, thin tail with a spade tip, glowing yellow eyes, dark plum tunic, friendly expression, holds a hoe in the work frames and a dark blade in the attack frames |
| D2 | a demon woman villager | violet skin, small horns, black hair in a bun, dark teal dress, thin tail, holds a hoe and a dark blade |
| D3 | an older demon | dusky orange skin, long swept-back horns, grey beard, brown robe, thin tail |
| D4 (optional) | a demon mage | magenta skin, small horns, deep blue hooded robe, holds an open book while reading and casts a small fireball in the attack frames |

### Animals
Change the frame list in the template as shown below:

| # | `<CHARACTER>` | `<DETAILS>` | Frame list to use instead |
|---|---|---|---|
| A1 | a fluffy sheep | cream wool with soft curls, dark grey face and legs | `idle 1, idle 2 (ear flick), walk 1–4, grazing with head down 1–2, lying down defeated` |
| A2 | a wild deer | warm brown coat, cream belly, white tail tuft, small antlers, long thin legs | `idle 1, idle 2, walk 1–4, grazing 1–2, lying down defeated` |
| A3 | a wild rabbit | sandy brown fur, cream belly, long ears, white cotton tail, small body | `idle 1, idle 2 (ear twitch), hop 1–4, nibbling 1–2, lying down defeated` |
| A4 | a grey wolf | grey fur with lighter belly, bushy tail, amber eyes, pointed ears | `idle 1, idle 2 (tail wag), run 1–4, eating 1–2, crouch before pouncing, biting lunge with open jaws, lying down defeated` |

### Dragon (frame size 64 × 64 px: change "32x32" to "64x64" in the template)
| # | `<CHARACTER>` | `<DETAILS>` | Frame list |
|---|---|---|---|
| X1 | a real four-legged dragon (not a dragon-human) | emerald green scales, cream belly plates, large bat-like wings with dark teal membranes, curved ivory horns, long tail with a spade tip, amber eyes | `idle 1, idle 2, walk 1–4 (wings flapping), eating with head down 1–2, rearing up, lunging bite with fire glowing in the open jaws, lying down defeated` |

---

## 4. Optional: front / back views
The game currently faces left or right only. If you want four directions later, create extra images per character:
```
... same character ..., three views in one row: front view facing the viewer, side view facing right, back view facing away.
```
Claude Code will have to update the renderer to use these, so tell it you want this.

## 5. Common problems and fixes
| Problem | Add to the prompt |
|---|---|
| blurry or soft edges | `hard pixel edges, nearest-neighbor upscale, no anti-aliasing, no smoothing` |
| pixels of uneven size | `every pixel exactly the same size, aligned to a strict pixel grid` |
| colours too smooth or glowing | `limited palette of about 24 flat colors, no gradients, no glow` |
| feet not level between frames | `all frames share the same ground line and the same character scale` |
| a background scene appears | `isolated sprite, flat solid magenta background #FF00FF only` |
| the face changes every frame | attach the image of the same character you made before, and add `exactly the same character design as the attached image` |

## 6. What to send to Claude Code
- The files: PNG if possible, named by the codes above, e.g. `H1_sheet.png`, `A4_idle.png`, `X1_walk.png`
- A note: which tool and settings you used (to record in the asset credits)
- Which ones should go into the game, and whether to keep or drop the old pixel art
