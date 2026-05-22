<script setup>
import { provide, reactive } from "vue";
import { useRoute } from "vue-router";
import { useGameView } from "../composables/useGameView";
import { gameConfigs } from "../configs/gameConfigs";
import { GAME_VIEW_KEY } from "../composables/game/injectKey";
import GameViewContainer from "../components/game/GameViewContainer.vue";

const route = useRoute();

const configMap = {
  genshin: gameConfigs.genshin,
  hsr: gameConfigs.hsr,
  zzz: gameConfigs.zzz,
  hi3: gameConfigs.hi3,
};

const config = configMap[route.params.game];
const gameView = reactive(useGameView(config));

provide(GAME_VIEW_KEY, gameView);
</script>

<template>
  <GameViewContainer />
</template>