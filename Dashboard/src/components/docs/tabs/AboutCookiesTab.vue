<script setup>
import Card from "primevue/card";
import DataTable from "primevue/datatable";
import Column from "primevue/column";
import Accordion from "primevue/accordion";
import AccordionPanel from "primevue/accordionpanel";
import AccordionHeader from "primevue/accordionheader";
import AccordionContent from "primevue/accordioncontent";
import Message from "primevue/message";

const cookieActions = [
  { action: "Login to your game account", canDo: false },
  { action: "Change your game account login information", canDo: false },
  { action: "Change your game account password", canDo: false },
  { action: "Delete your game account", canDo: false },
  { action: "Sell your game account", canDo: false },
  { action: "Login to your HoYoLAB Account", canDo: true },
  {
    action: "Make posts, comments, replies with your HoYoLAB Account",
    canDo: true,
  },
  {
    action:
      "Obtain your in-game information through Battle Chronicles (Character Builds, Real-time Notes, Exploration Progress etc.)",
    canDo: true,
  },
  { action: "Redeem Codes", canDo: true },
];

const faqItems = [
  {
    question: "Does HoYoLAB Cookies expire?",
    answer:
      "Yes. They expire after 1 year after its initial creation, which is when you login to HoYoLAB",
  },
  {
    question: "Can I manually invalidate my HoYoLAB Cookies?",
    answer:
      "Yes, changing your HoYoverse account password will immediately invalidate all HoYoLAB Cookies",
  },
  {
    question: "How are you using my HoYoLAB Cookies?",
    answer:
      "We use the public API provided by HoYoLAB to access your information. These public API requires your HoYoLAB Cookies, alongside your HoYoLAB UID, as they require you to be logged in when accessing the data",
  },
];
</script>

<template>
  <div class="flex flex-col gap-6">
    <Card class="bg-white/5 border border-white/10 rounded-2xl">
      <template #content>
        <h2 class="text-3xl font-bold tracking-tight text-zinc-100 mb-2">
          HoYoLAB Cookies
        </h2>
        <p class="text-zinc-300 leading-relaxed">
          Understanding how Mehrak uses your HoYoLAB cookies
        </p>
      </template>
    </Card>

    <Card class="bg-white/5 border border-white/10 rounded-2xl">
      <template #content>
        <h3 class="text-2xl font-semibold tracking-tight text-zinc-100 mb-4">
          Disclaimer
        </h3>
        <p class="text-zinc-300 leading-relaxed mb-4">
          Mehrak does not force its users to provide their HoYoLAB Cookies
          without consent. While many features of Mehrak requires the use of
          HoYoLAB Cookies, it is on the user's choice to provide their HoYoLAB
          Cookies with consent. Should the user choose to withdraw their consent
          and wish to remove their information from Mehrak's database, please
          refer to FAQ and Privacy Policy on how this could be done
        </p>
        <p class="text-zinc-300 leading-relaxed">
          By providing your HoYoLAB Cookies, you consent to our
          <a
            href="https://gameryuan.gitbook.io/mehrak/resources/privacy-policy"
            target="_blank"
            rel="noopener noreferrer"
            class="text-emerald-400 hover:text-emerald-300 transition-colors"
            >Privacy Policy</a
          >
        </p>
      </template>
    </Card>

    <Card class="bg-white/5 border border-white/10 rounded-2xl">
      <template #content>
        <h3 class="text-2xl font-semibold tracking-tight text-zinc-100 mb-4">
          What are cookies?
        </h3>
        <p class="text-zinc-300 leading-relaxed mb-4">
          Cookies are traces of information left on your browser when you access
          online services. They generally contain information about your login
          session, your preferences, or other temporary information related to
          you
        </p>
        <p class="text-zinc-300 leading-relaxed mb-4">
          Imagine you have a stamp book. When you visit a store, the storekeeper
          gave you a stamp. This stamp helps the storekeeper remember you and
          what you like to buy from their store. The next time you visit the
          store, the storekeeper can immediately recognise you based on the
          stamp they gave you and recommend you new products that you may like
        </p>
        <p class="text-zinc-300 leading-relaxed mb-4">
          Your browser is the stamp book, and cookies are the stamps. Most
          websites leave cookies on your browser to help them remember that
          you've been here before, which improves your convenience when visiting
          that website for the second time
        </p>
        <p class="text-zinc-300 leading-relaxed">
          In our case, HoYoLAB Cookies contain your session information. This is
          what kept you logged in to HoYoLAB. Mehrak requires this cookie as it
          uses services provided by HoYoLAB for its services
        </p>
      </template>
    </Card>

    <Card class="bg-white/5 border border-white/10 rounded-2xl">
      <template #content>
        <h3 class="text-2xl font-semibold tracking-tight text-zinc-100 mb-4">
          What can HoYoLAB Cookies do?
        </h3>
        <p class="text-zinc-300 leading-relaxed mb-4">
          The HoYoLAB Cookies Mehrak requires contains your login session. These
          are the things that you can and cannot do with said HoYoLAB Cookies
        </p>

        <DataTable :value="cookieActions" class="bg-transparent">
          <Column
            field="action"
            header="Actions"
            class="bg-white/5! border-white/10! text-zinc-300!"
          ></Column>
          <Column
            field="canDo"
            header=""
            class="bg-white/5! border-white/10! w-20!"
          >
            <template #body="{ data }">
              <span v-if="data.canDo" class="text-emerald-400 text-lg">✅</span>
              <span v-else class="text-red-400 text-lg">❌</span>
            </template>
          </Column>
        </DataTable>

        <p class="text-zinc-300 leading-relaxed mt-4">
          In short, the cookies provided can only be used to access services
          provided by HoYoLAB, and cannot be used for other services provided by
          HoYoverse
        </p>
      </template>
    </Card>

    <Card class="bg-white/5 border border-white/10 rounded-2xl">
      <template #content>
        <h3 class="text-2xl font-semibold tracking-tight text-zinc-100 mb-4">
          How are my Cookies stored?
        </h3>
        <p class="text-zinc-300 leading-relaxed mb-4">
          The official Mehrak bot runs on a Virtual Private Server (VPS) hosted
          by Hetzner. Several best practices has been done to ensure that the
          VPS instance is kept secure from unauthorised access
        </p>
        <p class="text-zinc-300 leading-relaxed mb-4">
          All your information are stored in a database hosted on the same VPS.
          Before being stored in the database, your provided passphrase is used
          to generate a AES-256 key to encrypt your cookie, ensuring that all
          cookies are securely stored with minimal risk of being cracked.
          Therefore, it is important that you choose a sufficiently strong and
          secure passphrase for each profile
        </p>
        <p class="text-zinc-300 leading-relaxed mb-4">
          After authenticating your passphrase for command execution, your
          cookies will be stored in a password-protected Redis instance. This
          allows you to use multiple commands within the same 5 minutes without
          requiring to type in your passphrase again. This cookie will be
          proactively evicted from the cache after the expiry period
        </p>
        <p class="text-zinc-300 leading-relaxed">
          This setup provides security in exchange for convenience. In the case
          of a data breach, your encrypted cookies might be obtained, but will
          be nearly uncrackable if you follow the best practices when creating a
          passphrase, rendering it useless to the intruder
        </p>
      </template>
    </Card>

    <Card class="bg-white/5 border border-white/10 rounded-2xl">
      <template #content>
        <h3 class="text-2xl font-semibold tracking-tight text-zinc-100 mb-4">
          Our commitment
        </h3>
        <p class="text-zinc-300 leading-relaxed mb-4">
          We, as the developer team, is committed to providing users with secure
          and convenient services. Should there be an identified security
          breach, we will make an announcement to all users in our Official
          Discord Server, along with steps to help you secure your HoYoLAB
          cookies
        </p>
        <p class="text-zinc-300 leading-relaxed">
          For security purposes, the official Mehrak developer team will not
          release more convenient methods of providing your HoYoLAB Cookies, as
          these convenient methods often involves executing proprietary scripts
          or logging into HoYoLAB with your real account username and password.
          These are easily hijackable to bring real security risks to our users.
          If any individual approaches you to offer "convenient" ways to provide
          HoYoLAB Cookies to Mehrak, we advise you to block communications with
          said individual and report said incident to the developers in our
          <a
            href="https://discord.gg/3GYvRD4u9a"
            target="_blank"
            rel="noopener noreferrer"
            class="text-emerald-400 hover:text-emerald-300 transition-colors"
            >Official Discord Server</a
          >
        </p>
      </template>
    </Card>

    <Card class="bg-white/5 border border-white/10 rounded-2xl">
      <template #content>
        <h3 class="text-2xl font-semibold tracking-tight text-zinc-100 mb-4">
          Other Facts about HoYoLAB Cookies
        </h3>

        <Accordion
          value=""
          multiple
          expandIcon="pi pi-chevron-down"
          collapseIcon="pi pi-chevron-up"
        >
          <AccordionPanel
            v-for="(item, index) in faqItems"
            :key="index"
            :value="index"
          >
            <AccordionHeader>{{ item.question }}</AccordionHeader>
            <AccordionContent>
              <p class="text-zinc-300 leading-relaxed m-0">{{ item.answer }}</p>
            </AccordionContent>
          </AccordionPanel>
        </Accordion>
      </template>
    </Card>
  </div>
</template>

<style scoped>
:deep(.p-datatable) {
  background: transparent;
  border-radius: 0.75rem;
  overflow: hidden;
}
:deep(.p-datatable-thead > tr > th) {
  background: rgba(255, 255, 255, 0.05);
  border-color: rgba(255, 255, 255, 0.1);
  color: #a1a1aa;
}
:deep(.p-datatable-tbody > tr > td) {
  background: transparent;
  border-color: rgba(255, 255, 255, 0.05);
  color: #d4d4d8;
}
:deep(.p-datatable-tbody > tr:hover > td) {
  background: rgba(255, 255, 255, 0.03);
}
</style>
