def set_rules(world):
      world.multiworld.completion_condition[world.player] = lambda state: state.has("Victory", world.player)