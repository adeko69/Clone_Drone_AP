"""Planification des upgrades par step (mode "Per Step").

Règles :
- une upgrade "(Step N)" ne peut se trouver que dans les locations des steps 1 à N
  (un step bas ne peut pas dépendre d'un step plus haut) ; elle n'est utilisable que pendant le step N (côté C#) ;
- un item ne peut pas être posé dans le check de sa propre upgrade, ni d'une upgrade qui en dépend
  (callable `forbidden(item, location)` fournie par l'appelant).

Fonction pure (sans Archipelago) pour pouvoir la tester seule.
"""
import sys

# Items globaux gardés en priorité quand les locations sont rares
GLOBAL_PRIORITY = ["ShopDiscount_1", "ShopDiscount_2", "ShopDiscount_3", "NextUpgradeFree"]


def plan_step_items(candidates: dict, step_locs: dict, rng, forbidden=None, reserve: int = 0) -> list:
    """candidates : {step: [noms d'items "X (Step k)", doublons possibles]}
    step_locs  : {step: [noms de locations libres du step]}
    forbidden  : callable(item, location) -> bool, ou None
    reserve    : nombre de locations à laisser libres pour les items globaux

    Retourne la liste [(nom de location, nom d'item)] à poser en pre_fill.

    Capacité : un step ne reçoit jamais plus d'items que ses propres locations, et au total on laisse
    `reserve` locations libres. L'affectation est ensuite faite par appariement (chemins augmentants) :
    elle trouve un placement valide dès qu'il en existe un, même avec les interdits.
    Les items qui n'ont vraiment aucune place sont abandonnés.
    """
    steps = sorted(candidates)
    total_free = sum(len(v) for v in step_locs.values())
    limit = max(0, total_free - reserve)

    counts = {k: min(len(candidates[k]), len(step_locs.get(k, []))) for k in steps}
    # L'épée (si elle est à trouver) n'est jamais rognée : sans elle le step démarre sans arme.
    floor = {k: min(counts[k], sum(1 for c in candidates[k] if c.startswith("SwordUnlock ("))) for k in steps}
    while sum(counts.values()) > limit:
        trimmable = [s for s in steps if counts[s] > floor[s]]
        if not trimmable:
            break
        k = max(trimmable, key=lambda s: (counts[s], s))   # on rogne le step le plus chargé (le plus haut à égalité)
        counts[k] -= 1

    items = []   # (step, nom)
    for k in steps:
        head = [c for c in candidates[k] if c.startswith("SwordUnlock (")]   # l'épée passe toujours en premier
        rest = [c for c in candidates[k] if not c.startswith("SwordUnlock (")]
        rng.shuffle(rest)
        for name in (head + rest)[:counts[k]]:
            items.append((k, name))

    # Locations permises pour chaque item : steps 1..k, hors interdits
    adjacency = []
    for k, name in items:
        allowed = [
            loc
            for j in steps if j <= k
            for loc in step_locs.get(j, [])
            if not (forbidden and forbidden(name, loc))
        ]
        rng.shuffle(allowed)
        adjacency.append(allowed)

    taken = {}   # location -> index d'item

    def assign(i, seen):
        for loc in adjacency[i]:
            if loc in seen:
                continue
            seen.add(loc)
            j = taken.get(loc)
            if j is None or assign(j, seen):
                taken[loc] = i
                return True
        return False

    order = list(range(len(items)))
    rng.shuffle(order)
    previous_limit = sys.getrecursionlimit()
    sys.setrecursionlimit(max(previous_limit, 5000))
    try:
        for i in order:
            assign(i, set())
    finally:
        sys.setrecursionlimit(previous_limit)

    return [(loc, items[i][1]) for loc, i in taken.items()]