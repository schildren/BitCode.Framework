#!/usr/bin/env node
import { execSync } from 'child_process';
import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(__dirname, '..');

const args = process.argv.slice(2);
const isNotesOnly = args.includes('--notes-only');
const isDryRun = args.includes('--dry-run');

function getGitOutput(command) {
  try {
    return execSync(command, { cwd: repoRoot, encoding: 'utf8' }).trim();
  } catch (error) {
    return '';
  }
}

function getLatestTag() {
  const tags = getGitOutput('git tag --sort=-creatordate').split('\n').filter(Boolean);
  return tags[0] || null;
}

function getCommits(fromRef, toRef = 'HEAD') {
  const range = fromRef ? `${fromRef}..${toRef}` : toRef;
  const logFormat = '%H%x1f%an%x1f%ad%x1f%s%x1f%b%x1e';
  const rawLog = getGitOutput(`git log "${range}" --pretty=format:"${logFormat}" --date=short`);

  if (!rawLog) return [];

  const rawEntries = rawLog.split('\x1e').map(e => e.trim()).filter(Boolean);
  const commits = [];

  for (const entry of rawEntries) {
    const [hash, author, date, subject, body] = entry.split('\x1f');
    if (!hash || !subject) continue;

    commits.push({
      hash: hash.substring(0, 7),
      fullHash: hash,
      author: author || '',
      date: date || '',
      subject: subject || '',
      body: body || ''
    });
  }

  return commits;
}

function parseConventionalCommit(commit) {
  const match = commit.subject.match(/^(\w+)(?:\(([^)]+)\))?(!)?:\s*(.*)$/);
  const isBreaking = commit.subject.includes('!') || commit.body.includes('BREAKING CHANGE');

  if (!match) {
    return {
      type: 'other',
      scope: null,
      description: commit.subject,
      isBreaking,
      raw: commit
    };
  }

  return {
    type: match[1].toLowerCase(),
    scope: match[2] || null,
    description: match[4],
    isBreaking,
    raw: commit
  };
}

function formatReleaseNotes(versionTitle, commits, previousTag) {
  const categorized = {
    breaking: [],
    features: [],
    fixes: [],
    performance: [],
    docs: [],
    architecture: [],
    other: []
  };

  for (const commit of commits) {
    const parsed = parseConventionalCommit(commit);

    if (parsed.isBreaking) {
      categorized.breaking.push(parsed);
    } else if (parsed.type === 'feat') {
      categorized.features.push(parsed);
    } else if (parsed.type === 'fix') {
      categorized.fixes.push(parsed);
    } else if (parsed.type === 'perf') {
      categorized.performance.push(parsed);
    } else if (parsed.type === 'docs') {
      categorized.docs.push(parsed);
    } else if (['refactor', 'test', 'ci', 'chore', 'infra', 'tools', 'governance'].includes(parsed.type)) {
      categorized.architecture.push(parsed);
    } else {
      categorized.other.push(parsed);
    }
  }

  const lines = [];
  lines.push(`## ${versionTitle} (${new Date().toISOString().split('T')[0]})`);
  lines.push('');

  function renderGroup(title, items) {
    if (items.length === 0) return;
    lines.push(`### ${title}`);
    lines.push('');
    for (const item of items) {
      const scopePrefix = item.scope ? `**${item.scope}**: ` : '';
      lines.push(`- ${scopePrefix}${item.description} ([${item.raw.hash}](https://github.com/schildren/BitCode.Framework/commit/${item.raw.fullHash}))`);
    }
    lines.push('');
  }

  renderGroup('💥 Breaking Changes', categorized.breaking);
  renderGroup('🚀 Nuevas Capacidades y Features', categorized.features);
  renderGroup('🐛 Correcciones y Bug Fixes', categorized.fixes);
  renderGroup('⚡ Rendimiento y Optimización', categorized.performance);
  renderGroup('📚 Documentación y Guías', categorized.docs);
  renderGroup('🏗️ Arquitectura, Tooling y Gobernanza', categorized.architecture);
  renderGroup('📦 Otros Cambios', categorized.other);

  return lines.join('\n');
}

function main() {
  const latestTag = getLatestTag();
  const commits = getCommits(latestTag, 'HEAD');

  const releaseVersion = latestTag ? `Próxima Versión (desde ${latestTag})` : 'v0.1.0';
  const notes = formatReleaseNotes(releaseVersion, commits, latestTag);

  if (isNotesOnly) {
    console.log(notes);
    return;
  }

  const changelogPath = path.join(repoRoot, 'CHANGELOG.md');
  let existingContent = '';

  if (fs.existsSync(changelogPath)) {
    existingContent = fs.readFileSync(changelogPath, 'utf8');
  }

  const header = '# Changelog — BitCode.Framework\n\nTodos los cambios notables en este framework se documentan en este archivo siguiendo [Conventional Commits](https://www.conventionalcommits.org/) y [Semantic Versioning](https://semver.org/).\n\n';

  let finalContent = '';
  if (existingContent.startsWith('# Changelog — BitCode.Framework')) {
    const afterHeader = existingContent.replace(/^# Changelog — BitCode\.Framework\n\n[^\n]+\n\n/, '');
    finalContent = header + notes + '\n' + afterHeader.trimStart();
  } else {
    finalContent = header + notes + '\n' + existingContent;
  }

  if (isDryRun) {
    console.log('[DRY-RUN] Contenido de CHANGELOG.md que se generaría:\n');
    console.log(finalContent);
  } else {
    fs.writeFileSync(changelogPath, finalContent, 'utf8');
    console.log(`CHANGELOG.md actualizado exitosamente en: ${changelogPath}`);
    console.log(`- Total de commits procesados: ${commits.length}`);
  }
}

main();
