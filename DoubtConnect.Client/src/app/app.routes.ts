
import { Routes } from '@angular/router';

import { HomeComponent } from './pages/home/home.component';
import { LoginComponent } from './pages/login/login.component';
import { RegisterComponent } from './pages/register/register.component';
import { ForgotPasswordComponent } from './pages/forgot-password/forgot-password.component';
import { ResetPasswordComponent } from './pages/reset-password/reset-password.component';
import { StudentDashboardComponent } from './pages/student-dashboard/student-dashboard.component';
import { MentorDashboardComponent } from './pages/mentor-dashboard/mentor-dashboard.component';

export const routes: Routes = [
  {
    path: '',
    component: HomeComponent
  },
  {
    path: 'home',
    component: HomeComponent
  },
  {
    path: 'login',
    component: LoginComponent
  },
  {
    path: 'register',
    component: RegisterComponent
  },
  {
    path: 'forgot-password',
    component: ForgotPasswordComponent
  },
  {
    path: 'reset-password',
    component: ResetPasswordComponent
  },
  {
    path: 'student-dashboard',
    component: StudentDashboardComponent
  },
  {
    path: 'mentor-dashboard',
    component: MentorDashboardComponent
  },
  {
    path: '**',
    redirectTo: ''
  }
];
