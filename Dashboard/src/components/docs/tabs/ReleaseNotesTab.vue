<script setup>
import { ref, computed, onMounted } from "vue";
import Card from "primevue/card";
import Button from "primevue/button";
import ProgressSpinner from "primevue/progressspinner";
import { useReleaseNotes } from "../../../composables/useReleaseNotes";

const { fetchAll } = useReleaseNotes();

const releases = ref([]);
const loading = ref(true);
const error = ref(null);

const getTypeOrder = (type) => {
  const order = { feature: 0, improvement: 1, fix: 2 };
  return order[type] ?? 3;
};

const getFirstCommand = (text) => {
  const match = text.match(/\[([^\]]+)\]/);
  return match ? match[1] : "\uFFFF";
};

const sortedReleases = computed(() => {
  return releases.value.map((release) => ({
    ...release,
    sections: release.sections.map((section) => ({
      ...section,
      notes: [...section.notes].sort((a, b) => {
        const typeOrderA = getTypeOrder(a.type);
        const typeOrderB = getTypeOrder(b.type);
        if (typeOrderA !== typeOrderB) {
          return typeOrderA - typeOrderB;
        }
        const cmdA = getFirstCommand(a.text);
        const cmdB = getFirstCommand(b.text);
        return cmdA.localeCompare(cmdB);
      }),
    })),
  }));
});

const selectedVersion = ref("");

const scrollToVersion = (version) => {
  selectedVersion.value = version;
  const element = document.getElementById(`release-${version}`);
  if (element) {
    element.scrollIntoView({ behavior: "smooth", block: "start" });
  }
};

const getTypeLabel = (type) => {
  const labels = {
    feature: { label: "Feature", class: "bg-emerald-500/20 text-emerald-300" },
    improvement: {
      label: "Improvement",
      class: "bg-blue-500/20 text-blue-300",
    },
    fix: { label: "Fix", class: "bg-orange-500/20 text-orange-300" },
  };
  return labels[type] || { label: type, class: "bg-zinc-500/20 text-zinc-300" };
};

const parseNoteText = (text) => {
  const parts = [];
  const regex = /\[([^\]]+)\]/g;
  let lastIndex = 0;
  let match;
  let hasCommands = false;

  while ((match = regex.exec(text)) !== null) {
    hasCommands = true;
    if (match.index > lastIndex) {
      parts.push({ type: "text", text: text.slice(lastIndex, match.index) });
    }
    parts.push({ type: "command", text: match[1] });
    lastIndex = match.index + match[0].length;
  }

  if (lastIndex < text.length) {
    parts.push({ type: "text", text: text.slice(lastIndex) });
  }

  if (!hasCommands) {
    return [{ type: "text", text: text }];
  }

  return parts;
};

const scrollToTop = () => {
  window.scrollTo({ top: 0, behavior: "smooth" });
};

onMounted(async () => {
  try {
    releases.value = await fetchAll();
    if (releases.value.length > 0) {
      selectedVersion.value = releases.value[0].version;
    }
  } catch (e) {
    error.value = "Failed to load release notes.";
  } finally {
    loading.value = false;
  }
});
</script>

<template>
  <div class="flex flex-col gap-6">
    <Card class="bg-white/5 border border-white/10 rounded-2xl">
      <template #content>
        <h2 class="text-3xl font-bold tracking-tight text-zinc-100 mb-2">
          Release Notes
        </h2>
        <p class="text-zinc-300 leading-relaxed">
          Track important documentation updates and behavior changes between bot
          releases
        </p>
      </template>
    </Card>

    <div v-if="loading" class="flex justify-center py-12">
      <ProgressSpinner />
    </div>

    <div v-else-if="error" class="text-center py-12 text-zinc-400">
      {{ error }}
    </div>

    <div v-else-if="!sortedReleases.length" class="text-center py-12 text-zinc-400">
      No release notes available.
    </div>

    <div v-else class="grid grid-cols-1 lg:grid-cols-[1fr_140px] gap-6">
      <div class="flex flex-col gap-4">
        <Card
          v-for="release in sortedReleases"
          :key="release.version"
          :id="`release-${release.version}`"
          class="bg-white/5 border border-white/10 rounded-2xl"
        >
          <template #content>
            <div class="flex flex-col gap-4">
              <div class="flex items-center justify-between">
                <h3 class="text-xl font-bold text-zinc-100 font-mono">
                  {{ release.version }}
                </h3>
                <span v-if="release.date" class="text-sm text-zinc-500">{{
                  release.date
                }}</span>
              </div>

              <div
                v-for="section in release.sections"
                :key="section.name"
                class="flex flex-col gap-3"
              >
                <h4
                  class="text-sm font-semibold text-zinc-400 uppercase tracking-wider border-b border-white/10 pb-2"
                >
                  {{ section.name }}
                </h4>
                <ul class="flex flex-col gap-2">
                  <li
                    v-for="(note, index) in section.notes"
                    :key="index"
                    class="flex items-start gap-3"
                  >
                    <div class="flex items-center gap-1.5 shrink-0 flex-wrap">
                      <span
                        :class="[
                          'text-xs font-semibold px-2 py-1 rounded',
                          getTypeLabel(note.type).class,
                        ]"
                      >
                        {{ getTypeLabel(note.type).label }}
                      </span>
                      <template
                        v-for="(part, pIndex) in parseNoteText(note.text)"
                        :key="pIndex"
                      >
                        <span
                          v-if="part.type === 'command'"
                          class="text-xs font-semibold px-1.5 py-1 rounded bg-violet-500/20 text-violet-300"
                        >
                          {{ part.text }}
                        </span>
                      </template>
                    </div>
                    <span class="text-zinc-300 leading-relaxed flex-1 min-w-0">
                      <template
                        v-for="(part, pIndex) in parseNoteText(note.text)"
                        :key="pIndex"
                      >
                        <span v-if="part.type === 'text'">{{ part.text }}</span>
                      </template>
                    </span>
                  </li>
                </ul>
              </div>
            </div>
          </template>
        </Card>
      </div>

      <aside class="lg:sticky lg:top-28 h-fit">
        <Card class="bg-white/5 border border-white/10 rounded-2xl">
          <template #content>
            <h4
              class="text-sm font-semibold text-zinc-400 uppercase tracking-wider mb-3"
            >
              Versions
            </h4>
            <nav
              class="flex flex-col gap-1 max-h-80 overflow-y-auto pr-1 scrollbar-thin scrollbar-thumb-white/10 scrollbar-track-transparent"
            >
              <button
                v-for="release in sortedReleases"
                :key="release.version"
                type="button"
                :class="[
                  'text-left px-2 py-1.5 rounded-lg text-sm transition-all',
                  selectedVersion === release.version
                    ? 'text-white bg-emerald-500/20 border border-emerald-500/50'
                    : 'text-zinc-400 hover:text-zinc-200 hover:bg-white/5',
                ]"
                @click="scrollToVersion(release.version)"
              >
                <span class="font-mono text-xs">{{ release.version }}</span>
              </button>
            </nav>
          </template>
        </Card>
      </aside>
    </div>

    <div class="fixed bottom-6 right-6 z-50">
      <Button
        icon="pi pi-arrow-up"
        rounded
        severity="secondary"
        class="bg-emerald-500/20! border-emerald-500/50! text-emerald-300! hover:bg-emerald-500/30! backdrop-blur-sm shadow-lg"
        @click="scrollToTop"
      >
      </Button>
    </div>
  </div>
</template>
