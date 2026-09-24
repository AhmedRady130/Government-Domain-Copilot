import { Routes } from '@angular/router';
import { DashboardComponent } from './features/dashboard/dashboard.component';
import { CopilotComponent } from './features/copilot/copilot.component';
import { ExplorerComponent } from './features/explorer/explorer.component';
export const routes: Routes = [{ path: '', component: DashboardComponent, title: 'Dashboard' }, { path: 'copilot', component: CopilotComponent, title: 'AI Copilot' }, { path: 'explorer', component: ExplorerComponent, title: 'API Explorer' }, { path: '**', redirectTo: '' }];
