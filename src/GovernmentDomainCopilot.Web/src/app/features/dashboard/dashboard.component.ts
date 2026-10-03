import { Component, OnInit, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { forkJoin } from 'rxjs';
import { ApiService } from '../../services/api.service';
@Component({ standalone: true, imports: [RouterLink], template: `
  <section class="heading"><div><p class="eyebrow">Evidence-grounded operations</p><h1>Dashboard</h1><p>Monitor the Government Domain Copilot API and navigate its tenant-scoped tools.</p></div><span class="badge" [class.badge-ok]="health() === 'Online'" [class.badge-fail]="health().startsWith('Unavailable')">{{ health() }}</span></section>
  <div class="cards"><article><h2>Server health</h2><strong>{{ health() }}</strong><p>Anonymous <code>GET /healthz</code></p><button (click)="refresh()">Refresh</button></article><article><h2>Active sessions</h2><strong>{{ metrics().sessions }}</strong><p>Current authenticated tenant</p></article><article><h2>Orchestration runs</h2><strong>{{ metrics().runs }}</strong><p>Latest 50 run records</p></article><article><h2>Pending approvals</h2><strong>{{ metrics().approvals }}</strong><p>Human approval workflow</p></article></div>
  <section class="panel"><h2>Quick actions</h2><div class="quick"><a routerLink="/copilot">Ask the AI Copilot</a><a routerLink="/explorer">Search evidence and manage API resources</a></div><p class="muted">This API exposes documents, grounded answers, orchestration, sessions, runs, approvals, search, and LLM traces. It does not contain separate Contract, Compliance, Policy Review, or Audit Log routes.</p></section>
` })
export class DashboardComponent implements OnInit {
  private readonly api = inject(ApiService); readonly health = signal('Checking…'); readonly metrics = signal({ sessions: 0, runs: 0, approvals: 0 });
  ngOnInit(): void { this.refresh(); }
  refresh(): void { this.health.set('Checking…'); forkJoin({ health: this.api.health(), sessions: this.api.sessions(), runs: this.api.runs(), approvals: this.api.approvals() }).subscribe({ next: value => { this.health.set(value.health.status === 'Healthy' ? 'Online' : value.health.status); this.metrics.set({ sessions: value.sessions.length, runs: value.runs.length, approvals: value.approvals.filter(x => x.decision === 'Pending').length }); }, error: error => this.health.set(`Unavailable: ${error.message}`) }); }
}
